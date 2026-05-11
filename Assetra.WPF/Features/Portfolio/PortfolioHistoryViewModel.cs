using System.Globalization;
using System.Windows.Media;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models;
using Assetra.Core.Models.Analysis;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// Provides chart data, period selection, and day-change metrics for the portfolio
/// history panel.  Owned by <see cref="PortfolioViewModel"/> as a child ViewModel.
/// </summary>
public sealed partial class PortfolioHistoryViewModel : ObservableObject
{
    private const int AllPeriodDays = 0;

    private readonly IPortfolioHistoryQueryService _historyQueryService;
    private readonly ILocalizationService _localization;
    private readonly IAppSettingsService? _settings;
    private readonly IMultiCurrencyValuationService? _fx;

    // Stage 1 (Dashboard consolidation plan)：可選的分析服務。為 null 時 KPI
    // 列降階為「只顯示絕對值」、對標列整段隱藏，不阻擋主圖渲染。
    private readonly IDrawdownCalculator? _drawdown;
    private readonly IBenchmarkComparisonService? _benchmark;
    // 「TWR + 交易」用於把區間報酬從 naive value-based 換成 full TWR
    // （考慮中間出入金）。兩個都注入時才會切換到 TWR 模式；任一缺則用 naive。
    private readonly ITimeWeightedReturnCalculator? _twr;
    private readonly ITradeRepository? _trades;

    /// <summary>Full snapshot history (all dates), cached on each DB load.</summary>
    private IReadOnlyList<PortfolioDailySnapshot> _allSnapshots = [];

    /// <summary>Full snapshot history exposed for Dashboard 10-day chart.</summary>
    public IReadOnlyList<PortfolioDailySnapshot> Snapshots => _allSnapshots;

    /// <summary>
    /// Stage 4 (Dashboard consolidation)：報酬日曆 sub-VM。在 LoadAsync 後
    /// 自動 push 最新 snapshot list。儀表板「報酬日曆」tab 透過 Binding 顯示。
    /// </summary>
    public Assetra.WPF.Features.FinancialOverview.Calendar.ReturnCalendarViewModel
        ReturnCalendar { get; } = new();

    // Chart series
    [ObservableProperty] private ISeries[] _valueSeries = [];
    [ObservableProperty] private ICartesianAxis[] _xAxes = [new Axis { IsVisible = false }];
    [ObservableProperty] private ICartesianAxis[] _yAxes = [new Axis { IsVisible = false }];

    // Period selection
    [ObservableProperty] private int _selectedDays = 30;

    // Custom range (overrides SelectedDays when both ends are set)
    [ObservableProperty] private DateTime? _customStartDate;
    [ObservableProperty] private DateTime? _customEndDate;

    /// <summary>
    /// Tag of the currently-active preset ("30"/"90"/"180"/"365"/"All"), or
    /// "Custom" when both ends of the custom range are set. Drives the active
    /// state of the Trends preset buttons.
    /// </summary>
    public string ActivePeriod =>
        (CustomStartDate, CustomEndDate) is ({ }, { })
            ? "Custom"
            : SelectedDays == AllPeriodDays
                ? "All"
                : SelectedDays.ToString(CultureInfo.InvariantCulture);

    partial void OnSelectedDaysChanged(int value) => OnPropertyChanged(nameof(ActivePeriod));

    partial void OnCustomStartDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(ActivePeriod));
        RefreshChart();
    }

    partial void OnCustomEndDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(ActivePeriod));
        RefreshChart();
    }

    // Visibility guards
    [ObservableProperty] private bool _hasHistory;
    [ObservableProperty] private bool _isChartVisible = true;
    [ObservableProperty] private bool _isHistoryPanelVisible;

    partial void OnHasHistoryChanged(bool value) => IsHistoryPanelVisible = HasHistory && IsChartVisible;
    partial void OnIsChartVisibleChanged(bool value) => IsHistoryPanelVisible = HasHistory && IsChartVisible;

    // Stage 1：區間 KPI（5 張卡 + 對標比較區）
    [ObservableProperty] private decimal _kpiStartValue;
    [ObservableProperty] private decimal _kpiEndValue;
    [ObservableProperty] private decimal _kpiAbsolutePnl;
    [ObservableProperty] private decimal _kpiReturnPct;        // 整個區間累積報酬率 (small "naive" calc：value-based)
    [ObservableProperty] private decimal _kpiAnnualizedPct;    // 年化
    [ObservableProperty] private decimal _kpiMaxDrawdownPct;   // 最大回撤 (負數，例：-0.12 表示 -12%)
    [ObservableProperty] private bool _hasKpis;
    /// <summary>True 當有提供 IDrawdownCalculator 時；用於控制最大回撤卡的可見性。</summary>
    [ObservableProperty] private bool _hasDrawdown;

    // Stage 1：對標比較（4 個固定 benchmark）
    // 每個都是「該對標期間 TWR」字串，未啟用 / 無歷史 = "—"
    [ObservableProperty] private string _benchmarkTaiexDisplay = "—";
    [ObservableProperty] private string _benchmarkTw0050Display = "—";
    [ObservableProperty] private string _benchmarkTw00981ADisplay = "—";
    [ObservableProperty] private string _benchmarkDeposit15Display = "—";   // 1.5% 年化參考（合成）
    /// <summary>True 當 IBenchmarkComparisonService 已注入。隱藏整個對標區用。</summary>
    [ObservableProperty] private bool _hasBenchmark;

    public PortfolioHistoryViewModel(
        IPortfolioHistoryQueryService historyQueryService,
        ILocalizationService? localization = null,
        IAppSettingsService? settings = null,
        IMultiCurrencyValuationService? fx = null,
        IDrawdownCalculator? drawdown = null,
        IBenchmarkComparisonService? benchmark = null,
        ITimeWeightedReturnCalculator? twr = null,
        ITradeRepository? trades = null)
    {
        _historyQueryService = historyQueryService;
        _localization = localization ?? NullLocalizationService.Instance;
        _settings = settings;
        _fx = fx;
        _drawdown = drawdown;
        _benchmark = benchmark;
        _twr = twr;
        _trades = trades;
        HasDrawdown = _drawdown is not null;
        HasBenchmark = _benchmark is not null;
    }

    // Public API

    /// <summary>Fetches snapshots from DB and rebuilds the chart.</summary>
    public async Task LoadAsync()
    {
        _allSnapshots = await _historyQueryService.GetSnapshotsAsync();
        OnPropertyChanged(nameof(Snapshots));
        // Stage 4：餵入報酬日曆 sub-VM；獨立 try-catch 以免破壞主流程。
        try { ReturnCalendar.UpdateSnapshots(_allSnapshots); }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* swallow */ }
        await RefreshChartAsync();
    }

    /// <summary>
    /// Called after a theme switch.  Rebuilds the chart series with fresh
    /// SkiaSharp colours read from the updated WPF resource dictionary.
    /// Does NOT hit the DB.
    /// </summary>
    public void OnThemeChanged() => RefreshChart();

    // Period command

    [RelayCommand]
    private async Task ChangePeriod(string? period)
    {
        if (string.Equals(period, "All", StringComparison.OrdinalIgnoreCase))
        {
            CustomStartDate = null;
            CustomEndDate = null;
            SelectedDays = AllPeriodDays;
            await RefreshChartAsync();
            return;
        }

        if (int.TryParse(period, out var days) && days > 0)
        {
            // Selecting a preset clears any custom range.
            CustomStartDate = null;
            CustomEndDate = null;
            SelectedDays = days;
            await RefreshChartAsync();
        }
    }

    // Chart building

    private async Task RefreshChartAsync()
    {
        var filtered = (CustomStartDate, CustomEndDate) is ({ } s, { } e)
            ? FilterByRange(_allSnapshots, s, e)
            : FilterByDays(_allSnapshots, SelectedDays);
        var points = await BuildPointsAsync(filtered);
        BuildChart(points);

        // Stage 1: KPI 列 + 對標。失敗不影響主圖。
        try
        {
            await UpdateKpisAsync(filtered, points).ConfigureAwait(false);
            await UpdateBenchmarksAsync(filtered).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 任何 KPI / benchmark 計算錯誤不應拖累主流程
            HasKpis = false;
        }
    }

    // ── Stage 1: 區間 KPI 計算 ─────────────────────────────────────────
    // 報酬率優先用 full TWR（ITimeWeightedReturnCalculator + 交易 cash flow），
    // 處理中間出入金的影響；服務未注入時回退到 naive value-based 計算。
    private async Task UpdateKpisAsync(
        IReadOnlyList<PortfolioDailySnapshot> filtered,
        IReadOnlyList<DateTimePoint> points)
    {
        if (points.Count < 2)
        {
            HasKpis = false;
            return;
        }

        var startValue = (decimal)points[0].Value!.Value;
        var endValue = (decimal)points[^1].Value!.Value;
        KpiStartValue = startValue;
        KpiEndValue = endValue;
        KpiAbsolutePnl = endValue - startValue;

        // 預設用 naive；若 TWR 服務 + 交易 repo 都注入則切換為 TWR。
        var naiveReturn = startValue == 0m ? 0m : (endValue - startValue) / startValue;
        KpiReturnPct = await TryComputeTwrAsync(filtered, points).ConfigureAwait(false) ?? naiveReturn;

        // 年化：用實際日數 + 365 day year
        var startDate = points[0].DateTime;
        var endDate = points[^1].DateTime;
        var days = Math.Max(1, (endDate - startDate).TotalDays);
        if (days >= 1 && startValue > 0m && endValue > 0m)
        {
            // (1 + r) ^ (365/days) − 1
            var growth = (double)(endValue / startValue);
            var annualized = Math.Pow(growth, 365.0 / days) - 1.0;
            KpiAnnualizedPct = (decimal)annualized;
        }
        else
        {
            KpiAnnualizedPct = 0m;
        }

        // 最大回撤（只在有 IDrawdownCalculator 時計算）
        if (_drawdown is not null && filtered.Count >= 2)
        {
            var series = filtered
                .OrderBy(s => s.SnapshotDate)
                .Select(s => (s.SnapshotDate, s.MarketValue))
                .ToList();
            KpiMaxDrawdownPct = _drawdown.ComputeMaxDrawdown(series) ?? 0m;
        }
        else
        {
            KpiMaxDrawdownPct = 0m;
        }

        HasKpis = true;
    }

    // ── Stage 1: 對標 TWR 計算 ─────────────────────────────────────────
    // 4 個固定 benchmark：加權指數 / 0050 / 00981A / 1.5% 定存（合成）。
    // 任一無歷史時對應字串顯示 "—"，不影響其他項。
    private async Task UpdateBenchmarksAsync(IReadOnlyList<PortfolioDailySnapshot> filtered)
    {
        if (_benchmark is null || filtered.Count < 2)
        {
            BenchmarkTaiexDisplay = BenchmarkTw0050Display =
                BenchmarkTw00981ADisplay = BenchmarkDeposit15Display = "—";
            return;
        }

        var startDate = filtered.OrderBy(s => s.SnapshotDate).First().SnapshotDate;
        var endDate = filtered.OrderByDescending(s => s.SnapshotDate).First().SnapshotDate;
        var period = new PerformancePeriod(startDate, endDate);

        // 1.5% 年化定存的合成報酬：(1.015)^(days/365) − 1
        var days = Math.Max(1, endDate.DayNumber - startDate.DayNumber);
        var depositTwr = (decimal)(Math.Pow(1.015, days / 365.0) - 1.0);
        BenchmarkDeposit15Display = FormatPct(depositTwr);

        // 三個 ETF / index — 平行抓
        var taiexTask = SafeBenchmarkAsync("^TWII", period);
        var tw0050Task = SafeBenchmarkAsync("0050.TW", period);
        var tw00981aTask = SafeBenchmarkAsync("00981A.TW", period);

        await Task.WhenAll(taiexTask, tw0050Task, tw00981aTask).ConfigureAwait(false);

        BenchmarkTaiexDisplay = FormatPct(taiexTask.Result);
        BenchmarkTw0050Display = FormatPct(tw0050Task.Result);
        BenchmarkTw00981ADisplay = FormatPct(tw00981aTask.Result);
    }

    private async Task<decimal?> SafeBenchmarkAsync(string symbol, PerformancePeriod period)
    {
        try
        {
            return await _benchmark!.ComputeBenchmarkTwrAsync(symbol, period).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// 嘗試用 full TWR 計算區間報酬率。需要兩個服務都注入；任一缺或交易資料
    /// 不足時回傳 null（caller fallback 到 naive 計算）。
    /// </summary>
    private async Task<decimal?> TryComputeTwrAsync(
        IReadOnlyList<PortfolioDailySnapshot> filtered,
        IReadOnlyList<DateTimePoint> points)
    {
        if (_twr is null || _trades is null || filtered.Count < 2)
            return null;

        try
        {
            // Valuations 用 filtered snapshots（已經過 chart filter 的 base 幣別轉換）
            // 為簡化，使用 snapshot 原始 MarketValue（單幣別 user 預設情境）。
            var valuations = filtered
                .OrderBy(s => s.SnapshotDate)
                .Select(s => (s.SnapshotDate, s.MarketValue))
                .ToList();

            var startDate = filtered.Min(s => s.SnapshotDate);
            var endDate = filtered.Max(s => s.SnapshotDate);
            var period = new PerformancePeriod(startDate, endDate);

            var allTrades = await _trades.GetAllAsync().ConfigureAwait(false);
            var flows = Assetra.Application.Analysis.PerformanceFlowBuilder.BuildPerformanceFlows(
                allTrades, period);

            return _twr.Compute(valuations, flows);
        }
        catch
        {
            return null;
        }
    }

    private static string FormatPct(decimal? pct) =>
        pct is null
            ? "—"
            : (pct.Value >= 0 ? "+" : "") + (pct.Value * 100m).ToString("F2", CultureInfo.InvariantCulture) + "%";

    private void RefreshChart()
    {
        AsyncHelpers.SafeFireAndForget(RefreshChartAsync, "PortfolioHistory.RefreshChart");
    }

    private static IReadOnlyList<PortfolioDailySnapshot> FilterByDays(
        IReadOnlyList<PortfolioDailySnapshot> all, int days)
    {
        if (days == AllPeriodDays)
            return all.OrderBy(s => s.SnapshotDate).ToList();

        if (all.Count == 0)
            return [];

        var latestSnapshotDate = all.Max(s => s.SnapshotDate);
        var cutoff = latestSnapshotDate.AddDays(-(days - 1));
        return all
            .Where(s => s.SnapshotDate >= cutoff)
            .OrderBy(s => s.SnapshotDate)
            .ToList();
    }

    private static IReadOnlyList<PortfolioDailySnapshot> FilterByRange(
        IReadOnlyList<PortfolioDailySnapshot> all, DateTime start, DateTime end)
    {
        var (lo, hi) = start <= end ? (start, end) : (end, start);
        var loDate = DateOnly.FromDateTime(lo);
        var hiDate = DateOnly.FromDateTime(hi);
        return all
            .Where(s => s.SnapshotDate >= loDate && s.SnapshotDate <= hiDate)
            .OrderBy(s => s.SnapshotDate)
            .ToList();
    }

    private async Task<IReadOnlyList<DateTimePoint>> BuildPointsAsync(
        IReadOnlyList<PortfolioDailySnapshot> snapshots)
    {
        var points = new List<DateTimePoint>(snapshots.Count);
        foreach (var snapshot in snapshots.OrderBy(s => s.SnapshotDate))
        {
            var value = await ConvertMarketValueToBaseAsync(snapshot);
            if (value is null)
                continue;

            points.Add(new DateTimePoint(
                snapshot.SnapshotDate.ToDateTime(TimeOnly.MinValue),
                (double)value.Value));
        }
        return points;
    }

    private async Task<decimal?> ConvertMarketValueToBaseAsync(PortfolioDailySnapshot snapshot)
    {
        var baseCurrency = _settings?.Current.BaseCurrency;
        if (_fx is null || string.IsNullOrWhiteSpace(baseCurrency))
            return snapshot.MarketValue;

        var fromCurrency = string.IsNullOrWhiteSpace(snapshot.Currency) ? "TWD" : snapshot.Currency;
        if (string.Equals(fromCurrency, baseCurrency, StringComparison.OrdinalIgnoreCase))
            return snapshot.MarketValue;

        try
        {
            return await _fx.ConvertAsync(
                snapshot.MarketValue,
                fromCurrency,
                baseCurrency,
                snapshot.SnapshotDate);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private void BuildChart(IReadOnlyList<DateTimePoint> points)
    {
        HasHistory = points.Count >= 1;
        if (points.Count == 0)
        {
            ValueSeries = [];
            return;
        }

        // Read theme colours fresh each time so the chart always matches
        // the current palette (Dark / Light / colour-scheme).
        var accentColor = GetSkColor("AppAccent", "#0078D4");
        var fillColor = accentColor.WithAlpha(32);
        var labelColor = GetSkColor("AppTextSecondary", "#787B86");
        var separatorColor = GetSkColor("AppBorderLight", "#2E2E2E");

        ValueSeries =
        [
            new LineSeries<DateTimePoint>
            {
                Values            = points,
                Name              = GetString("Portfolio.History.MarketValue", "Market Value"),
                Stroke            = new SolidColorPaint(accentColor, 2),
                Fill              = new SolidColorPaint(fillColor),
                GeometrySize      = 4,
                GeometryFill      = new SolidColorPaint(accentColor),
                GeometryStroke    = new SolidColorPaint(accentColor, 1),
                LineSmoothness    = 0,
                AnimationsSpeed   = TimeSpan.Zero,
            }
        ];

        XAxes =
        [
            new DateTimeAxis(TimeSpan.FromDays(1), date => date.ToString("MM/dd"))
            {
                TextSize        = 10,
                LabelsPaint     = new SolidColorPaint(labelColor),
                SeparatorsPaint = new SolidColorPaint(separatorColor),
                TicksPaint      = null,
            }
        ];

        YAxes =
        [
            new Axis
            {
                Position        = LiveChartsCore.Measure.AxisPosition.End,
                TextSize        = 10,
                LabelsPaint     = new SolidColorPaint(labelColor),
                SeparatorsPaint = new SolidColorPaint(separatorColor),
                TicksPaint      = null,
                Labeler         = v => v.ToString("N0"),
            }
        ];
    }

    // Colour helpers

    /// <summary>
    /// Reads a <see cref="SolidColorBrush"/> from the WPF application resources and
    /// converts it to an <see cref="SKColor"/>.  Falls back to <paramref name="hexFallback"/>
    /// if the resource is not found (e.g., in unit-test contexts without a UI).
    /// </summary>
    private static SKColor GetSkColor(string key, string hexFallback)
    {
        if (System.Windows.Application.Current?.TryFindResource(key) is SolidColorBrush brush)
        {
            var c = brush.Color;
            return new SKColor(c.R, c.G, c.B, c.A);
        }
        return SKColor.Parse(hexFallback);
    }

    private string GetString(string key, string fallback) =>
        _localization.Get(key, fallback);
}
