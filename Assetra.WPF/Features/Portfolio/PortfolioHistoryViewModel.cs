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
    // 風險指標：原本 Reports 的 Risk Expander 內容，搬到 Trends 後共用。
    private readonly IVolatilityCalculator? _volatility;
    private readonly ISharpeRatioCalculator? _sharpe;
    private readonly IConcentrationAnalyzer? _concentration;
    private readonly IStockSearchService? _search;   // 對比 picker autocomplete（＝新增資產用的搜尋）
    private readonly Assetra.Core.Interfaces.Analysis.IGroupPerformanceSeriesService? _groupPerformance; // 群組比較線
    private readonly Assetra.WPF.Features.PortfolioGroups.PortfolioGroupCatalog? _groupCatalog;          // 群組名稱/清單

    /// <summary>Full snapshot history (all dates), cached on each DB load.</summary>
    private IReadOnlyList<PortfolioDailySnapshot> _allSnapshots = [];

    /// <summary>Full snapshot history exposed for Dashboard 10-day chart.</summary>
    public IReadOnlyList<PortfolioDailySnapshot> Snapshots => _allSnapshots;

    /// <summary>
    /// Stage 4 (Dashboard consolidation)：報酬日曆 sub-VM。在 LoadAsync 後
    /// 自動 push 最新 snapshot list。儀表板「報酬日曆」tab 透過 Binding 顯示。
    /// </summary>
    public Assetra.WPF.Features.FinancialOverview.Calendar.ReturnCalendarViewModel
        ReturnCalendar
    { get; }

    // Chart series — ValueSeries（絕對淨值）給投資資產概覽迷你圖；CompareSeries（% 比較）給資產趨勢頁。
    [ObservableProperty] private ISeries[] _valueSeries = [];
    [ObservableProperty] private ICartesianAxis[] _xAxes = [new Axis { IsVisible = false }];
    [ObservableProperty] private ICartesianAxis[] _yAxes = [new Axis { IsVisible = false }];
    [ObservableProperty] private ISeries[] _compareSeries = [];
    [ObservableProperty] private ICartesianAxis[] _compareXAxes = [new Axis { IsVisible = false }];
    [ObservableProperty] private ICartesianAxis[] _compareYAxes = [new Axis { IsVisible = false }];

    /// <summary>對標疊線用的「大盤」symbol — 加權指數（Yahoo ^TWII；router 已導向 Yahoo）。</summary>
    private const string BenchmarkOverlaySymbol = "^TWII";

    // ── 「＋比較」picker：資產趨勢圖的比較項目（我的投組／大盤／股票，全部使用者自選、可移除、預設空）──
    /// <summary>我的投組（整體 TWR）在比較清單中的特殊 token。</summary>
    public const string PortfolioItemToken = "@me";

    /// <summary>群組比較 token 前綴；完整格式「@group:{guid}」。</summary>
    public const string GroupTokenPrefix = "@group:";

    [ObservableProperty] private string _comparisonInput = string.Empty;

    private IReadOnlyList<string> CurrentComparisonItems =>
        _settings?.Current.ComparisonItems ?? [];

    public bool CanAddComparison =>
        !string.IsNullOrWhiteSpace(ComparisonInput)
        && CurrentComparisonItems.Count < 6
        && !CurrentComparisonItems.Any(s => string.Equals(s, ComparisonInput.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>我的投組是否還沒在比較清單（控制 popup 內快速加入鈕）。</summary>
    public bool CanAddMyPortfolio =>
        CurrentComparisonItems.Count < 6
        && !CurrentComparisonItems.Contains(PortfolioItemToken);

    /// <summary>大盤（加權指數）是否還沒在比較清單。</summary>
    public bool CanAddMarket =>
        CurrentComparisonItems.Count < 6
        && !CurrentComparisonItems.Any(s => string.Equals(s, BenchmarkOverlaySymbol, StringComparison.OrdinalIgnoreCase));

    /// <summary>對比 picker 的 autocomplete 候選（邊打邊出，最多 8 筆）。</summary>
    [ObservableProperty] private IReadOnlyList<StockSearchResult> _comparisonSuggestions = [];

    /// <summary>「＋比較」搜尋 popup 是否開啟。</summary>
    [ObservableProperty] private bool _isComparisonPickerOpen;

    partial void OnComparisonInputChanged(string value)
    {
        OnPropertyChanged(nameof(CanAddComparison));
        AddComparisonCommand.NotifyCanExecuteChanged();
        ComparisonSuggestions = string.IsNullOrWhiteSpace(value) || _search is null
            ? []
            : _search.Search(value.Trim()).Take(8).ToList();
    }

    [RelayCommand]
    private void OpenComparisonPicker()
    {
        ComparisonInput = string.Empty;
        ComparisonSuggestions = [];
        IsComparisonPickerOpen = true;
    }

    /// <summary>加入一個比較 token（去重、上限 6）；持久化＋重畫。</summary>
    private async Task AddComparisonTokenAsync(string token)
    {
        if (_settings is null || string.IsNullOrWhiteSpace(token))
            return;
        token = token.Trim();
        if (CurrentComparisonItems.Count >= 6
            || CurrentComparisonItems.Any(s => string.Equals(s, token, StringComparison.OrdinalIgnoreCase)))
            return;
        var list = CurrentComparisonItems.ToList();
        list.Add(token);
        await PersistComparisonItemsAsync(list).ConfigureAwait(true);
        RefreshChart();
    }

    [RelayCommand(CanExecute = nameof(CanAddMyPortfolio))]
    private Task AddMyPortfolioAsync() => AddComparisonTokenAsync(PortfolioItemToken);

    [RelayCommand(CanExecute = nameof(CanAddMarket))]
    private Task AddMarketAsync() => AddComparisonTokenAsync(BenchmarkOverlaySymbol);

    /// <summary>「＋比較」popup 列出的群組（投資組合 bucket，供快速加入為比較線）。catalog 未注入時為 null。</summary>
    public System.Collections.ObjectModel.ReadOnlyObservableCollection<PortfolioGroup>? ComparisonGroupOptions =>
        _groupCatalog?.Groups;

    [RelayCommand]
    private async Task AddGroupAsync(PortfolioGroup? group)
    {
        if (group is null)
            return;
        await AddComparisonTokenAsync(GroupTokenPrefix + group.Id).ConfigureAwait(true);
        IsComparisonPickerOpen = false;
    }

    [RelayCommand]
    private async Task SelectComparisonSuggestionAsync(StockSearchResult? r)
    {
        if (r is null)
            return;
        await AddComparisonTokenAsync(FormatBenchmarkSymbol(r)).ConfigureAwait(true);
        ComparisonInput = string.Empty;
        ComparisonSuggestions = [];
        IsComparisonPickerOpen = false;
    }

    /// <summary>
    /// 搜尋結果 → benchmark 抓取用 symbol：TWSE→.TW、TPEX→.TWO、其餘（NASDAQ／NYSE 等美股）保留交易所
    /// 後綴「{symbol}.{exchange}」。SplitSymbol 會還原出交易所，DynamicHistoryProvider 對 NYSE/NASDAQ 等
    /// 外國交易所自動 route 到 Yahoo。先前美股漏掉後綴 → SplitSymbol 預設 TW → 抓不到。
    /// </summary>
    private static string FormatBenchmarkSymbol(StockSearchResult r) => r.Exchange switch
    {
        "TWSE" => $"{r.Symbol}.TW",
        "TPEX" => $"{r.Symbol}.TWO",
        _ => string.IsNullOrWhiteSpace(r.Exchange) ? r.Symbol : $"{r.Symbol}.{r.Exchange}",
    };

    [RelayCommand(CanExecute = nameof(CanAddComparison))]
    private async Task AddComparisonAsync()
    {
        await AddComparisonTokenAsync(ComparisonInput.Trim()).ConfigureAwait(true);
        ComparisonInput = string.Empty;
    }

    [RelayCommand]
    private async Task RemoveComparisonAsync(string? token)
    {
        if (_settings is null || string.IsNullOrWhiteSpace(token))
            return;
        var list = CurrentComparisonItems
            .Where(s => !string.Equals(s, token, StringComparison.OrdinalIgnoreCase))
            .ToList();
        await PersistComparisonItemsAsync(list).ConfigureAwait(true);
        RefreshChart();
    }

    private async Task PersistComparisonItemsAsync(IReadOnlyList<string> items)
    {
        try
        {
            // raiseChanged:false — 由本 VM 自己 RefreshChart 更新圖，避免觸發 app-wide reload。
            await _settings!
                .SaveAsync(_settings.Current with { ComparisonItems = items.ToList() }, raiseChanged: false)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[Trends] 儲存比較項目失敗");
        }
        OnPropertyChanged(nameof(CanAddComparison));
        OnPropertyChanged(nameof(CanAddMyPortfolio));
        OnPropertyChanged(nameof(CanAddMarket));
        AddComparisonCommand.NotifyCanExecuteChanged();
        AddMyPortfolioCommand.NotifyCanExecuteChanged();
        AddMarketCommand.NotifyCanExecuteChanged();
    }

    /// <summary>比較圖的圖例（chips；每項 label／顏色／移除 token）；空清單時為空。</summary>
    [ObservableProperty] private IReadOnlyList<ComparisonLegendItem> _comparisonLegend = [];

    /// <summary>是否有任何比較項目；false 時資產趨勢圖顯示「點 ＋比較 選擇要比較的項目」提示。</summary>
    [ObservableProperty] private bool _hasComparisonItems;

    /// <summary>下方比較清單：每項 ＋ 截至顯示日的同期報酬 %（自區間起點起算）。隨滑鼠 hover 更新。</summary>
    [ObservableProperty] private IReadOnlyList<ComparisonRow> _comparisonRows = [];

    /// <summary>下方清單顯示的「截至日期」（hover 到的那天；null＝期末）。圖表 hover 由 code-behind 設定。</summary>
    [ObservableProperty] private DateTime? _comparisonHoverDate;

    /// <summary>下方清單的「截至 YYYY/MM/DD」文字（hover 中那天 / 期末）。</summary>
    [ObservableProperty] private string _comparisonAsOfText = string.Empty;

    /// <summary>各比較線的點資料（供下方清單依 hover 日取值）。</summary>
    private IReadOnlyList<(string Label, string ColorHex, string RemoveToken, IReadOnlyList<DateTimePoint> Points)> _comparisonLines = [];

    partial void OnComparisonHoverDateChanged(DateTime? value) => UpdateComparisonRows();

    /// <summary>依目前顯示日（hover 或期末）重算下方清單每項的同期 %。</summary>
    private void UpdateComparisonRows()
    {
        if (_comparisonLines.Count == 0)
        {
            ComparisonRows = [];
            ComparisonAsOfText = string.Empty;
            return;
        }
        var rows = new List<ComparisonRow>(_comparisonLines.Count);
        DateTime usedDate = default;
        foreach (var (label, color, token, pts) in _comparisonLines)
        {
            if (pts.Count == 0)
                continue;
            var pt = ComparisonHoverDate is { } d
                ? pts.LastOrDefault(p => p.DateTime <= d) ?? pts[0]
                : pts[^1];
            usedDate = pt.DateTime;
            var pct = pt.Value ?? 0d;
            // 漲跌色：PnlColorPalette 已依 ColorSchemeService 處理台股「漲紅跌綠」慣例（pct 為比例 → ×100 成百分比）。
            rows.Add(new ComparisonRow(label, color, token, pct, Assetra.WPF.Infrastructure.PnlColorPalette.Pick(pct * 100d)));
        }
        ComparisonRows = rows;
        ComparisonAsOfText = usedDate == default
            ? string.Empty
            : usedDate.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>對比線配色：我的投組固定取 [0] 藍；其餘對標從 [1] 開始循環。固定色（兩主題皆可讀）。</summary>
    private static readonly string[] ComparisonPalette =
    {
        "#2563EB", "#F59E0B", "#14B8A6", "#8B5CF6", "#EC4899", "#10B981",
    };

    // Period selection
    [ObservableProperty] private int _selectedDays = 30;

    /// <summary>
    /// P5.9 — explicit chip key, separate from SelectedDays. Needed because YTD
    /// computes a variable day count (e.g., 146 on May 26) but the chip should
    /// stay highlighted as "YTD" not "146". Set by <c>ChangePeriod</c>; default
    /// "30" matches initial SelectedDays = 30.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActivePeriod))]
    private string _activePeriodKey = "30";

    // Custom range (overrides SelectedDays when both ends are set)
    [ObservableProperty] private DateTime? _customStartDate;
    [ObservableProperty] private DateTime? _customEndDate;

    /// <summary>
    /// Tag of the currently-active chip ("5"/"30"/"180"/"YTD"/"365"/"1825"/"All"),
    /// or "Custom" when both ends of the custom range are set. Drives chip
    /// IsChecked binding.
    /// </summary>
    public string ActivePeriod =>
        (CustomStartDate, CustomEndDate) is ({ }, { })
            ? "Custom"
            : ActivePeriodKey;

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
    [ObservableProperty] private decimal _kpiMaxDrawdownPct;   // 最大回撤 (DrawdownCalculator 回正值表 magnitude)
    [ObservableProperty] private bool _hasKpis;
    /// <summary>True 當有提供 IDrawdownCalculator 時；用於控制最大回撤卡的可見性。</summary>
    [ObservableProperty] private bool _hasDrawdown;

    // 風險指標（之前在 Reports.Risk Expander，搬到 Trends 共用）
    [ObservableProperty] private decimal _kpiVolatilityPct;     // 年化波動率
    [ObservableProperty] private decimal _kpiSharpeRatio;       // Sharpe 比率（無單位，F2 顯示）
    [ObservableProperty] private decimal _kpiHhi;               // 集中度（0..1，F3 顯示）
    [ObservableProperty] private bool _hasVolatility;
    [ObservableProperty] private bool _hasSharpe;
    [ObservableProperty] private bool _hasHhi;

    // Stage 1：對標比較（4 個固定 benchmark）
    // 每個都是「該對標期間 TWR」字串，未啟用 / 無歷史 = "—"
    [ObservableProperty] private string _benchmarkTaiexDisplay = "—";
    [ObservableProperty] private string _benchmarkTw0050Display = "—";
    [ObservableProperty] private string _benchmarkTw00981ADisplay = "—";
    [ObservableProperty] private string _benchmarkDeposit15Display = "—";   // 1.5% 年化參考（合成）
    /// <summary>True 當 IBenchmarkComparisonService 已注入。隱藏整個對標區用。</summary>
    [ObservableProperty] private bool _hasBenchmark;

    /// <summary>
    /// 對標區實際比對的時間範圍顯示字串（如「2026-04-12 ~ 2026-05-12 · 30 天」）。
    /// 由 UpdateBenchmarksAsync 在每次計算前依照 filtered snapshots 的首末日設定，
    /// 讓使用者知道「同期」是哪段期間。</summary>
    [ObservableProperty] private string _benchmarkPeriodDisplay = "—";

    /// <summary>
    /// 「可用資料 X 天」hint：snapshot 表實際涵蓋的日數。
    /// 用於告訴使用者 chip（30 / 90 / 180 / 365）若超過此值，畫面會 clamp 到實際範圍。
    /// 為空字串時 UI 隱藏 hint（首次 mount 前）。
    /// </summary>
    [ObservableProperty] private string _dataRangeHint = string.Empty;

    // v2：使用者自訂對標。每個項目是 (symbol, display) — UI 用 ObservableCollection
    // 綁定，序列化結果直接從 AppSettings.CustomBenchmarkSymbols 拉。最多 4 個。
    private readonly System.Collections.ObjectModel.ObservableCollection<Assetra.WPF.Features.FinancialOverview.CustomBenchmarkRow> _customBenchmarks = [];
    public System.Collections.ObjectModel.ReadOnlyObservableCollection<Assetra.WPF.Features.FinancialOverview.CustomBenchmarkRow> CustomBenchmarks { get; }

    // P5：上次套用的自訂對標清單快照，用來在 IAppSettingsService.Changed 觸發時判斷
    // CustomBenchmarkSymbols 是否真的變了（避免每次無關設定儲存都重跑對標）。
    private IReadOnlyList<string> _lastBenchmarkSymbols;

    public PortfolioHistoryViewModel(
        IPortfolioHistoryQueryService historyQueryService,
        ILocalizationService? localization = null,
        IAppSettingsService? settings = null,
        IMultiCurrencyValuationService? fx = null,
        IDrawdownCalculator? drawdown = null,
        IBenchmarkComparisonService? benchmark = null,
        ITimeWeightedReturnCalculator? twr = null,
        ITradeRepository? trades = null,
        IVolatilityCalculator? volatility = null,
        ISharpeRatioCalculator? sharpe = null,
        IConcentrationAnalyzer? concentration = null,
        IStockSearchService? search = null,
        Assetra.Core.Interfaces.Analysis.IGroupPerformanceSeriesService? groupPerformance = null,
        Assetra.WPF.Features.PortfolioGroups.PortfolioGroupCatalog? groupCatalog = null)
    {
        _historyQueryService = historyQueryService;
        _localization = localization ?? NullLocalizationService.Instance;
        _settings = settings;
        ReturnCalendar = new Assetra.WPF.Features.FinancialOverview.Calendar.ReturnCalendarViewModel(_settings);
        _fx = fx;
        _drawdown = drawdown;
        _benchmark = benchmark;
        _twr = twr;
        _trades = trades;
        _volatility = volatility;
        _sharpe = sharpe;
        _concentration = concentration;
        _search = search;
        _groupPerformance = groupPerformance;
        _groupCatalog = groupCatalog;
        HasDrawdown = _drawdown is not null;
        HasBenchmark = _benchmark is not null;
        HasVolatility = _volatility is not null;
        HasSharpe = _sharpe is not null;
        HasHhi = _concentration is not null;
        CustomBenchmarks = new System.Collections.ObjectModel.ReadOnlyObservableCollection<Assetra.WPF.Features.FinancialOverview.CustomBenchmarkRow>(_customBenchmarks);

        // P5：記住建構當下的自訂對標清單；之後 Changed 觸發時與此比對偵測變動。
        _lastBenchmarkSymbols = (_settings?.Current.CustomBenchmarkSymbols ?? new List<string>()).ToList();

        // 還原使用者上次選的走勢圖期間（一次性；之後由 ChangePeriod 負責持久化）。
        var savedPeriod = _settings?.Current.PortfolioHistoryPeriod;
        if (!string.IsNullOrWhiteSpace(savedPeriod))
            ApplyPeriodKey(savedPeriod);

        // app-lifetime singleton（由 PortfolioViewModel 持有），無 Dispose 生命週期；
        // 訂閱隨 app 存活，與 PortfolioViewModel 對 Changed 的訂閱相同模式。
        if (_settings is not null)
            _settings.Changed += OnSettingsChanged;
    }

    // Public API

    /// <summary>Fetches snapshots from DB and rebuilds the chart.</summary>
    public async Task LoadAsync()
    {
        _allSnapshots = await _historyQueryService.GetSnapshotsAsync();
        OnPropertyChanged(nameof(Snapshots));

        // 算「可用資料 X 天」hint — 以實際涵蓋日數（end - start + 1）為準，
        // 而不是 count（snapshot 可能跳過假日）。供 chip 區顯示，讓使用者知道
        // 90/180/365 chip 為什麼可能看起來跟 30 一樣。
        if (_allSnapshots.Count >= 2)
        {
            var minDate = _allSnapshots.Min(s => s.SnapshotDate);
            var maxDate = _allSnapshots.Max(s => s.SnapshotDate);
            var spanDays = maxDate.DayNumber - minDate.DayNumber + 1;
            DataRangeHint = $"可用資料 {spanDays} 天";
        }
        else if (_allSnapshots.Count == 1)
        {
            DataRangeHint = "可用資料 1 天";
        }
        else
        {
            DataRangeHint = string.Empty;
        }

        // Stage 4：餵入報酬日曆 sub-VM。日曆仍以 daily snapshot 市值變化為主；
        // 交易只用於同日買入/賣出/股利 cash-flow 調整，避免本金進出被看成報酬。
        IReadOnlyList<Trade> allTrades = [];
        if (_trades is not null)
        {
            try
            { allTrades = await _trades.GetAllAsync(); }
            catch (Exception ex) when (ex is not OperationCanceledException) { allTrades = []; }
        }
        try
        { ReturnCalendar.UpdatePortfolioData(_allSnapshots, allTrades); }
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
        if (string.IsNullOrWhiteSpace(period))
            return;

        // 任何 preset 都先清掉 custom range
        CustomStartDate = null;
        CustomEndDate = null;

        if (!ApplyPeriodKey(period))
            return;

        await RefreshChartAsync();
        // 持久化使用者選的期間（raiseChanged: false —— 純 UI 記帳，不觸發全 App reload）。
        _ = PersistPeriodAsync();
    }

    /// <summary>
    /// 套用期間 chip key 到 <see cref="ActivePeriodKey"/> + <see cref="SelectedDays"/>，不重繪、不持久化。
    /// 供 ChangePeriod（使用者點選）與建構子還原共用。回傳 false = 無法識別的 key。
    /// </summary>
    private bool ApplyPeriodKey(string period)
    {
        if (string.Equals(period, "All", StringComparison.OrdinalIgnoreCase))
        {
            ActivePeriodKey = "All";
            SelectedDays = AllPeriodDays;
            return true;
        }

        // P5.9 — YTD: 從今年 1/1 算到今天，動態天數但 chip 顯示為 "YTD" key
        if (string.Equals(period, "YTD", StringComparison.OrdinalIgnoreCase))
        {
            var today = DateTime.Today;
            var jan1 = new DateTime(today.Year, 1, 1);
            ActivePeriodKey = "YTD";
            SelectedDays = Math.Max(1, (today - jan1).Days + 1);
            return true;
        }

        if (int.TryParse(period, out var days) && days > 0)
        {
            ActivePeriodKey = period;
            SelectedDays = days;
            return true;
        }

        return false;
    }

    private async Task PersistPeriodAsync()
    {
        if (_settings is null)
            return;
        try
        {
            await _settings.SaveAsync(_settings.Current with { PortfolioHistoryPeriod = ActivePeriodKey }, raiseChanged: false);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[Trends] 持久化走勢圖期間失敗");
        }
    }

    // P5.1 — 「重算這天」/ partial-price 修復按鈕移除。原本是針對「曲線只算
    // 投資 MarketValue 時某日缺價就跳水」的 workaround；現在曲線改用真實淨值
    // (Cash + Equity − Liability fallback MarketValue) 後，假象問題消失，
    // workaround 也跟著移除。

    // Chart building

    private async Task RefreshChartAsync()
    {
        var filtered = ComputeFilteredSnapshots();
        var points = await BuildPointsAsync(filtered);

        // 比較視圖（資產趨勢頁）：使用者自選的比較項目（我的投組 TWR ＋ 大盤 ＋ 股票），預設空。
        // 不 ConfigureAwait(false)：BuildChart / BuildComparePercentChart 需回 UI thread 設 series。
        IReadOnlyList<(string Label, string ColorHex, string RemoveToken, IReadOnlyList<DateTimePoint> Points)> compareLines = [];
        if (filtered.Count >= 2 && points.Count >= 2)
            compareLines = await BuildComparisonLinesAsync(filtered);

        BuildChart(points);                     // 絕對淨值 → ValueSeries（概覽迷你圖）
        BuildComparePercentChart(compareLines); // % 比較 → CompareSeries（資產趨勢頁）

        // Stage 1: KPI 列 + 對標。失敗不影響主圖。
        try
        {
            await UpdateKpisAsync(filtered, points).ConfigureAwait(false);
            // 註：舊「同期對標報酬率」固定表的 XAML 已移除，但其計算（UpdateBenchmarksAsync）＋顯示屬性
            // 暫時保留（無人綁定），整組舊 benchmark 子系統的移除列為 M2 清理，避免本輪牽動其測試。
            await UpdateBenchmarksAsync(filtered).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 任何 KPI / benchmark 計算錯誤不應拖累主流程
            HasKpis = false;
        }
    }

    private IReadOnlyList<PortfolioDailySnapshot> ComputeFilteredSnapshots() =>
        (CustomStartDate, CustomEndDate) is ({ } s, { } e)
            ? FilterByRange(_allSnapshots, s, e)
            : FilterByDays(_allSnapshots, SelectedDays);

    // ── P5: 自訂對標清單即時生效 ───────────────────────────────────────────
    //
    // 使用者在設定頁改 CustomBenchmarkSymbols 時，若正停在 Trends 頁，對標比較區
    // 原本要等切換期間 / 重載才會用新清單重算（UpdateBenchmarksAsync 只在 chart
    // refresh 流程被呼叫）。訂閱 IAppSettingsService.Changed，偵測到清單實際變動就
    // 只重跑對標（不重建整張圖 / KPI），讓改完設定即時看到新對標列。
    private void OnSettingsChanged()
    {
        var current = _settings?.Current.CustomBenchmarkSymbols ?? new List<string>();
        if (CustomBenchmarkSymbolsEqual(current, _lastBenchmarkSymbols))
            return;
        _lastBenchmarkSymbols = current.ToList();
        AsyncHelpers.SafeFireAndForget(RefreshBenchmarksAsync, "PortfolioHistory.RefreshBenchmarksOnSettingsChanged");
    }

    private async Task RefreshBenchmarksAsync()
    {
        try
        {
            await UpdateBenchmarksAsync(ComputeFilteredSnapshots()).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 對標重算失敗不應影響其餘 UI；維持原本顯示。
        }
    }

    private static bool CustomBenchmarkSymbolsEqual(
        IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
            return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    // ── Stage 1: 區間 KPI 計算 ─────────────────────────────────────────
    // 報酬率優先用 full TWR（ITimeWeightedReturnCalculator + 交易 cash flow），
    // 處理中間出入金的影響；服務未注入時回退到 naive value-based 計算。
    /// <summary>剝掉領頭「建倉假象」低值點（median×0.05），回 cleaned (date,value) 序列。TWR／波動率／回撤共用。</summary>
    private static IReadOnlyList<(DateOnly Date, decimal Value)> BuildCleanedValuations(
        IReadOnlyList<PortfolioDailySnapshot> filtered)
    {
        var raw = filtered
            .OrderBy(s => s.SnapshotDate)
            .Select(s => (s.SnapshotDate, s.MarketValue))
            .ToList();
        if (raw.Count == 0)
            return raw;
        var median = raw.Select(s => s.MarketValue).OrderBy(v => v).ElementAt(raw.Count / 2);
        var threshold = median * 0.05m;
        var first = 0;
        while (first < raw.Count - 1 && raw[first].MarketValue < threshold)
            first++;
        return raw.Skip(first).ToList();
    }

    /// <summary>
    /// 投組角度 cash flow（Buy 正 / Sell 負）；null 交易服務回空。供 KPI 端點 TWR 與對比線 TWR 序列共用。
    /// </summary>
    private async Task<IReadOnlyList<Assetra.Core.Models.Analysis.CashFlow>> BuildPortfolioFlowsAsync(
        DateOnly start, DateOnly end)
    {
        if (_trades is null)
            return [];
        var allTrades = await _trades.GetAllAsync().ConfigureAwait(false);
        var rawFlows = Assetra.Application.Analysis.PerformanceFlowBuilder.BuildPerformanceFlows(
            allTrades, new PerformancePeriod(start, end));

        // ⚠ 符號慣例對齊：PerformanceFlowBuilder 的 flow 是「投資人現金角度」（Buy 負、Sell 正）；
        //   TimeWeightedReturnCalculator 跑在「MarketValue 序列」上需「投資組合角度」（Buy 正、Sell 負）。
        //   兩者相反 → 統一 negate 一次。不修 Builder（其 contract 是投資人角度，XIRR 等依賴之）。
        //   漏 negate 會讓 Buy 被當 2 倍貢獻、算出 6234% 荒謬值。
        return rawFlows
            .Select(f => new Assetra.Core.Models.Analysis.CashFlow(f.Date, -f.Amount, f.Currency))
            .ToList();
    }

    /// <summary>
    /// 「我的投組」對比線的 TWR % 點（每日累積 TWR）。服務缺／序列不足 → null（caller fallback 裸淨值%）。
    /// </summary>
    private async Task<IReadOnlyList<DateTimePoint>?> BuildPortfolioTwrPercentPointsAsync(
        IReadOnlyList<PortfolioDailySnapshot> filtered)
    {
        if (_twr is null || _trades is null)
            return null;
        var vals = BuildCleanedValuations(filtered);
        if (vals.Count < 2)
            return null;
        var flows = await BuildPortfolioFlowsAsync(vals[0].Date, vals[^1].Date).ConfigureAwait(false);
        var series = _twr.ComputeSeries(vals, flows);
        if (series is null || series.Count < 2)
            return null;
        return series
            .Select(p => new DateTimePoint(p.Date.ToDateTime(TimeOnly.MinValue), (double)p.CumulativeTwr))
            .ToList();
    }

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

        // 用 naive return 先填，TWR 服務若可用再 refine 覆寫
        var naiveReturn = startValue == 0m ? 0m : (endValue - startValue) / startValue;
        KpiReturnPct = naiveReturn;

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

        // ⚠ 雙保險：(1) 強制 reset 觸發 PropertyChanged false→true，繞過 ObservableProperty
        // 的相等性短路；(2) marshal 回 UI thread 確保 binding 接收得到通知。
        // 初次載入鏈經過數個 ConfigureAwait(false) await，到這裡可能在 threadpool；
        // 若同時 HasKpis 已是 true（不大可能但理論上有），單純 set true 不會 fire。
        // 強制 false→true 確保訊號一定走出去。
        InvokeOnUi(() =>
        {
            HasKpis = false;
            HasKpis = true;
        });

        // 跟 BuildPointsAsync 一致 — 剝掉領頭的「建倉假象」低值點，避免波動率/TWR 算出
        // 20000% 那種荒謬數字（從 $0 跳到 $8.8M 的單日 return 是 +infinity）。
        // 移到 TWR 之前計算，TWR 也使用這個 cleaned series。
        var series = BuildCleanedValuations(filtered);

        // 進階：TWR refine 報酬率（涵蓋現金流影響）— 用 cleaned series 而非 raw filtered，
        // 避免領頭低值點把 segment return 放大到幾千 %。
        var twrRefined = await TryComputeTwrAsync(series).ConfigureAwait(false);
        if (twrRefined.HasValue)
            KpiReturnPct = twrRefined.Value;

        // 最大回撤（只在有 IDrawdownCalculator 時計算）
        KpiMaxDrawdownPct = _drawdown is not null && series.Count >= 2
            ? _drawdown.ComputeMaxDrawdown(series) ?? 0m
            : 0m;

        // 年化波動率
        KpiVolatilityPct = _volatility is not null && series.Count >= 2
            ? _volatility.ComputeAnnualized(series) ?? 0m
            : 0m;

        // Sharpe = (annualizedReturn − 2%) / volatility；參考 Reports 用 0.02 risk-free rate
        if (_sharpe is not null && KpiVolatilityPct > 0m)
            KpiSharpeRatio = _sharpe.Compute(KpiAnnualizedPct, KpiVolatilityPct, riskFreeRate: 0.02m) ?? 0m;
        else
            KpiSharpeRatio = 0m;

        // HHI 集中度（async；失敗 fallback 為 0）
        if (_concentration is not null)
        {
            try
            {
                KpiHhi = await _concentration.ComputeHhiAsync().ConfigureAwait(false) ?? 0m;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                KpiHhi = 0m;
            }
        }
        else
        {
            KpiHhi = 0m;
        }
        // HasKpis 已在第一個 await 之前設好（避免初次渲染 bug），這裡不再重設
    }

    // ── Stage 1: 對標 TWR 計算 ─────────────────────────────────────────
    // 4 個固定 benchmark：加權指數 / 0050 / 00981A / 1.5% 定存（合成）。
    // 任一無歷史時對應字串顯示 "—"，不影響其他項。
    private async Task UpdateBenchmarksAsync(IReadOnlyList<PortfolioDailySnapshot> filtered)
    {
        if (_benchmark is null || filtered.Count < 2)
        {
            InvokeOnUi(() =>
            {
                BenchmarkTaiexDisplay = BenchmarkTw0050Display =
                    BenchmarkTw00981ADisplay = BenchmarkDeposit15Display = "—";
                BenchmarkPeriodDisplay = "—";
                _customBenchmarks.Clear();
            });
            return;
        }

        var startDate = filtered.OrderBy(s => s.SnapshotDate).First().SnapshotDate;
        var endDate = filtered.OrderByDescending(s => s.SnapshotDate).First().SnapshotDate;
        var period = new PerformancePeriod(startDate, endDate);

        // 1.5% 年化定存的合成報酬：(1.015)^(days/365) − 1
        // 注意：annualize 用 segment 天數（end - start）；UI 顯示用 inclusive 天數（+1）
        // 以對齊 chip 區的「可用資料 N 天」hint（兩處同樣定義成 calendar day 含頭含尾）。
        var segmentDays = Math.Max(1, endDate.DayNumber - startDate.DayNumber);
        var inclusiveDays = segmentDays + 1;
        var depositTwr = (decimal)(Math.Pow(1.015, segmentDays / 365.0) - 1.0);

        // 期間文字：例「2026-04-12 ~ 2026-05-12 · 23 天」。實際使用 filtered 的首末日，
        // 因為 snapshot 可能稀疏，跟 chip 上的「近 30 天」不一定完全對應。
        var periodText = $"{startDate:yyyy-MM-dd} ~ {endDate:yyyy-MM-dd} · {inclusiveDays} 天";

        // 三個 ETF / index — 平行抓
        var taiexTask = SafeBenchmarkAsync("^TWII", period);
        var tw0050Task = SafeBenchmarkAsync("0050.TW", period);
        var tw00981aTask = SafeBenchmarkAsync("00981A.TW", period);

        await Task.WhenAll(taiexTask, tw0050Task, tw00981aTask).ConfigureAwait(false);

        var taiexText = FormatPct(taiexTask.Result);
        var tw0050Text = FormatPct(tw0050Task.Result);
        var tw00981aText = FormatPct(tw00981aTask.Result);
        var depositText = FormatPct(depositTwr);

        // v2：自訂對標清單。最多 4 個避免畫面爆掉；每抓一個 TWR 都 try-catch。
        var custom = _settings?.Current.CustomBenchmarkSymbols ?? new List<string>();
        var customRows = new List<Assetra.WPF.Features.FinancialOverview.CustomBenchmarkRow>();
        foreach (var symbol in custom.Take(4))
        {
            if (string.IsNullOrWhiteSpace(symbol))
                continue;
            var twr = await SafeBenchmarkAsync(symbol, period).ConfigureAwait(false);
            customRows.Add(new Assetra.WPF.Features.FinancialOverview.CustomBenchmarkRow(
                Symbol: symbol,
                Display: FormatPct(twr)));
        }

        // 一次 marshal 回 UI thread：避免 cross-thread 漏 binding 通知 +
        // ObservableCollection mutate 在非 UI thread 會 throw。
        InvokeOnUi(() =>
        {
            BenchmarkPeriodDisplay = periodText;
            BenchmarkTaiexDisplay = taiexText;
            BenchmarkTw0050Display = tw0050Text;
            BenchmarkTw00981ADisplay = tw00981aText;
            BenchmarkDeposit15Display = depositText;
            _customBenchmarks.Clear();
            foreach (var row in customRows)
                _customBenchmarks.Add(row);
        });
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
        IReadOnlyList<(DateOnly Date, decimal Value)> valuations)
    {
        if (_twr is null || _trades is null || valuations.Count < 2)
            return null;

        try
        {
            // 注意：valuations 必須已經剝掉領頭低值點，否則 segment return 會爆。
            // caller (UpdateKpisAsync) 已經處理過了。
            var startDate = valuations[0].Date;
            var endDate = valuations[^1].Date;
            // 投組角度 cash flow（Buy 正 / Sell 負）；negate 與其原因見 BuildPortfolioFlowsAsync。
            var flows = await BuildPortfolioFlowsAsync(startDate, endDate).ConfigureAwait(false);

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

    private static bool HasCompleteBreakdown(PortfolioDailySnapshot s) =>
        s.CashValue.HasValue && s.EquityValue.HasValue;

    private async Task<IReadOnlyList<DateTimePoint>> BuildPointsAsync(
        IReadOnlyList<PortfolioDailySnapshot> snapshots)
    {
        // P5.2 — 略過缺 breakdown 的早期快照（缺歷史價日），避免基準跳動。
        // 只要系列中存在至少一個完整 breakdown 快照，就跳過沒有 CashValue/EquityValue 的列。
        // 若全部都是舊格式（無 breakdown），保留 MarketValue fallback，讓圖表不空白。
        var hasAnyBreakdown = snapshots.Any(HasCompleteBreakdown);

        var raw = new List<(DateTime When, decimal Val)>(snapshots.Count);
        foreach (var snapshot in snapshots.OrderBy(s => s.SnapshotDate))
        {
            if (hasAnyBreakdown && !HasCompleteBreakdown(snapshot))
                continue;
            var value = await ConvertMarketValueToBaseAsync(snapshot);
            if (value is null)
                continue;
            raw.Add((snapshot.SnapshotDate.ToDateTime(TimeOnly.MinValue), value.Value));
        }
        if (raw.Count == 0)
            return [];

        // ── 跳過「期初建倉」假象點 ─────────────────────────────────────
        // 問題：建倉日前後快照從 $0 跳到 $8.8M，會讓 KPI 區間報酬率算成「無限大」
        // 然後被 startValue==0 守衛壓成 0%，造成「期間賺很多但顯示 0%」的誤導。
        // 解法：以中位數的 5% 為門檻，剝掉前面所有「值 < 中位數 × 0.05」的領頭點。
        // 這保留正常的小幅波動（不會誤殺真實低值），但濾掉建倉初期假象。
        var median = raw.Select(p => p.Val).OrderBy(v => v).ElementAt(raw.Count / 2);
        var threshold = median * 0.05m;
        var firstValidIdx = 0;
        while (firstValidIdx < raw.Count - 1 && raw[firstValidIdx].Val < threshold)
            firstValidIdx++;

        var points = new List<DateTimePoint>(raw.Count - firstValidIdx);
        for (var i = firstValidIdx; i < raw.Count; i++)
            points.Add(new DateTimePoint(raw[i].When, (double)raw[i].Val));
        return points;
    }

    /// <summary>
    /// P5.1 — 每點數值改算真實淨值（跟總覽「投資焦點」widget 公式對齊）：
    /// <list type="bullet">
    ///   <item><description>v0.30+ snapshot（含 CashValue + EquityValue）：
    ///     <c>Cash + Equity − Liability</c></description></item>
    ///   <item><description>舊版 snapshot fallback：<c>MarketValue</c>（migration 期間混用）</description></item>
    /// </list>
    /// 再走 FX 換算到 base currency。原 partial-price snapshot 假象（某日 MV
    /// 跳水）會被新公式自動覆蓋 — 因為 Cash + Liability 通常不跳，所以淨值
    /// 即使遇到缺價也只小幅波動。「重算這天」修復按鈕也跟著移除。
    /// </summary>
    private async Task<decimal?> ConvertMarketValueToBaseAsync(PortfolioDailySnapshot snapshot)
    {
        var rawValue = ResolveNetWorthValue(snapshot);

        var baseCurrency = _settings?.Current.BaseCurrency;
        if (_fx is null || string.IsNullOrWhiteSpace(baseCurrency))
            return rawValue;

        var fromCurrency = string.IsNullOrWhiteSpace(snapshot.Currency) ? "TWD" : snapshot.Currency;
        if (string.Equals(fromCurrency, baseCurrency, StringComparison.OrdinalIgnoreCase))
            return rawValue;

        try
        {
            return await _fx.ConvertAsync(
                rawValue,
                fromCurrency,
                baseCurrency,
                snapshot.SnapshotDate);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// P5.1 — 跟 <c>DashboardViewModel.ResolveNetWorthValue</c> 同公式
    /// （投資焦點 widget 也用這個）：CashValue + EquityValue − LiabilityValue；
    /// 三欄缺值（v0.29 以前 snapshot）時 fallback 為 MarketValue。
    /// </summary>
    private static decimal ResolveNetWorthValue(PortfolioDailySnapshot s)
    {
        if (s.CashValue.HasValue && s.EquityValue.HasValue)
        {
            var equity = s.EquityValue.Value;
            var cash = s.CashValue.Value;
            var liab = s.LiabilityValue ?? 0m;
            return cash + equity - liab;
        }
        return s.MarketValue;
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
        var labelColor = GetSkColor("AppTextSecondary", "#787B86");
        // P2.16 — Grid 進一步降透明度。AppBorderLight 本身已 muted，再乘 0.30 alpha
        // 讓 separator 線在 dark / light 兩個 theme 都成 30% 強度可見但不搶 stroke。
        var separatorColor = GetSkColor("AppBorderLight", "#2E2E2E").WithAlpha(76);

        var fillColor = accentColor.WithAlpha(32);

        // P2.13 — Stroke 從 2px 降到 1.5px、GeometrySize 從 4 降到 3、
        // GeometryStroke 取消（純 fill 圓點）— 整體更貼近現代金融儀表板的細
        // 線質感，跟 audit「更淡更精緻、避免過重邊框」對齊。Fill alpha 32
        // 已是非常淡（12.5%），不再動。
        ValueSeries =
        [
            new LineSeries<DateTimePoint>
            {
                Values            = points,
                Name              = GetString("Portfolio.History.MarketValue", "Market Value"),
                Stroke            = new SolidColorPaint(accentColor, 1.5f),
                Fill              = new SolidColorPaint(fillColor),
                GeometrySize      = 3,
                GeometryFill      = new SolidColorPaint(accentColor),
                GeometryStroke    = null,
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

    /// <summary>
    /// 依使用者選的比較項目（ComparisonItems）逐項建一條 % 線：「@me」＝我的投組 TWR、其餘＝
    /// benchmark 正規化 % 序列。我的投組固定 palette[0]；個別抓不到就略過該條（主圖不受影響）。
    /// </summary>
    private async Task<IReadOnlyList<(string Label, string ColorHex, string RemoveToken, IReadOnlyList<DateTimePoint> Points)>>
        BuildComparisonLinesAsync(IReadOnlyList<PortfolioDailySnapshot> filtered)
    {
        var items = CurrentComparisonItems.Where(t => !string.IsNullOrWhiteSpace(t)).Take(6).ToList();
        var lines = new List<(string, string, string, IReadOnlyList<DateTimePoint>)>();
        if (items.Count == 0)
            return lines;

        var period = new PerformancePeriod(filtered.Min(s => s.SnapshotDate), filtered.Max(s => s.SnapshotDate));

        // 我的投組 TWR 只在 @me 在清單時算一次。
        IReadOnlyList<DateTimePoint>? mePts = null;
        if (items.Any(t => string.Equals(t, PortfolioItemToken, StringComparison.OrdinalIgnoreCase)))
            mePts = await BuildPortfolioTwrPercentPointsAsync(filtered).ConfigureAwait(false);

        var colorIdx = 1; // palette[0] 保留給我的投組
        foreach (var token in items)
        {
            if (string.Equals(token, PortfolioItemToken, StringComparison.OrdinalIgnoreCase))
            {
                if (mePts is { Count: >= 2 })
                    lines.Add((GetString("Portfolio.History.MeLabel", "我的投組"), ComparisonPalette[0], token, mePts));
                continue;
            }

            // 群組 token：@group:{guid} → 該群組的同期 % TWR 序列（重建持倉×價格）。
            if (token.StartsWith(GroupTokenPrefix, StringComparison.Ordinal)
                && Guid.TryParse(token[GroupTokenPrefix.Length..], out var gid))
            {
                if (_groupPerformance is null)
                    continue;
                try
                {
                    var gseries = await _groupPerformance.ComputeGroupSeriesAsync(gid, period).ConfigureAwait(false);
                    if (gseries is null || gseries.Count < 2)
                        continue;
                    var gpts = gseries
                        .Select(p => new DateTimePoint(p.Date.ToDateTime(TimeOnly.MinValue), (double)p.PercentFromStart))
                        .ToList();
                    var glabel = _groupCatalog?.FindById(gid)?.Name
                        ?? GetString("Portfolio.History.GroupFallback", "組合");
                    lines.Add((glabel, ComparisonPalette[colorIdx++ % ComparisonPalette.Length], token, gpts));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // 群組重建失敗 → 略過該條
                }
                continue;
            }

            if (_benchmark is null)
                continue;
            try
            {
                var series = await _benchmark.ComputeBenchmarkSeriesAsync(token, period).ConfigureAwait(false);
                if (series is null || series.Count < 2)
                    continue;
                var pts = series
                    .Select(p => new DateTimePoint(p.Date.ToDateTime(TimeOnly.MinValue), (double)p.PercentFromStart))
                    .ToList();
                lines.Add((LabelForToken(token), ComparisonPalette[colorIdx++ % ComparisonPalette.Length], token, pts));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 單一項目抓取失敗 → 略過該條
            }
        }
        return lines;
    }

    /// <summary>比較項目的顯示標籤：^TWII → 加權指數；其餘原樣 symbol。</summary>
    private string LabelForToken(string token) =>
        string.Equals(token, BenchmarkOverlaySymbol, StringComparison.OrdinalIgnoreCase)
            ? GetString("Portfolio.History.BenchmarkTaiex", "加權指數")
            : token;

    /// <summary>
    /// 比較圖：把使用者選的每個項目各畫一條 % 線（我的投組 TWR ＋ 大盤 ＋ 股票），全部可移除。
    /// 空清單 → 清空 series ＋ HasComparisonItems=false（XAML 顯示提示）。
    /// </summary>
    private void BuildComparePercentChart(
        IReadOnlyList<(string Label, string ColorHex, string RemoveToken, IReadOnlyList<DateTimePoint> Points)> lines)
    {
        HasComparisonItems = lines.Count > 0;
        if (lines.Count == 0)
        {
            CompareSeries = [];
            ComparisonLegend = [];
            _comparisonLines = [];
            UpdateComparisonRows();
            return;
        }

        var labelColor = GetSkColor("AppTextSecondary", "#787B86");
        var separatorColor = GetSkColor("AppBorderLight", "#2E2E2E").WithAlpha(76);

        var series = new List<ISeries>();
        var legend = new List<ComparisonLegendItem>();
        foreach (var (label, colorHex, removeToken, pts) in lines)
        {
            series.Add(new LineSeries<DateTimePoint>
            {
                Values          = pts,
                Name            = label,
                Stroke          = new SolidColorPaint(SKColor.Parse(colorHex), 2f),
                Fill            = null,
                GeometrySize    = 0,
                LineSmoothness  = 0,
                AnimationsSpeed = TimeSpan.Zero,
                // tooltip 顯示精確 %（+0.69%）而非 LiveCharts 預設的粗略 -1% / -0%。
                YToolTipLabelFormatter = p =>
                    (p.Coordinate.PrimaryValue >= 0 ? "+" : "")
                    + (p.Coordinate.PrimaryValue * 100d).ToString("F2", CultureInfo.InvariantCulture) + "%",
            });
            legend.Add(new ComparisonLegendItem(label, colorHex, removeToken));
        }

        CompareSeries = [.. series];
        ComparisonLegend = legend;
        _comparisonLines = lines;
        ComparisonHoverDate = null; // 重畫後預設顯示期末
        UpdateComparisonRows();

        CompareXAxes =
        [
            new DateTimeAxis(TimeSpan.FromDays(1), date => date.ToString("MM/dd"))
            {
                TextSize        = 10,
                LabelsPaint     = new SolidColorPaint(labelColor),
                SeparatorsPaint = new SolidColorPaint(separatorColor),
                TicksPaint      = null,
            }
        ];

        CompareYAxes =
        [
            new Axis
            {
                Position        = LiveChartsCore.Measure.AxisPosition.End,
                TextSize        = 10,
                LabelsPaint     = new SolidColorPaint(labelColor),
                SeparatorsPaint = new SolidColorPaint(separatorColor),
                TicksPaint      = null,
                Labeler         = v => (v >= 0 ? "+" : "") + (v * 100).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "%",
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

    /// <summary>
    /// 強制把 action 跑在 UI thread 上。用於 marshal 在 threadpool 上完成的 KPI 計算
    /// 結果回 UI，避免 PropertyChanged 在 view 首次 mount 階段漏掉。
    /// 沒 Dispatcher（test/headless 環境）就 fallback 直接執行。
    /// </summary>
    private static void InvokeOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }
}
