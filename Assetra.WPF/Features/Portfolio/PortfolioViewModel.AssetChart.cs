using Assetra.Core.Models;
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
/// P4.5 partial — asset detail panel 內聯價格圖。
/// <para>
/// 透過 <see cref="Assetra.Core.Interfaces.IStockHistoryProvider"/> 抓 close
/// price OHLCV，繪成 LiveCharts line series。可切換顯示模式：
/// <list type="bullet">
///   <item><description><b>price</b>：純 close price 走勢</description></item>
///   <item><description><b>myvalue</b>：每日 close × <b>當天實際持倉</b>（P4.8 走 trade
///     journal 重播：Buy / StockDividend +qty、Sell −qty），展示「我這部位的
///     市值走勢」。第一筆 Buy 之前的 OHLCV 點 qty=0 會被濾掉，所以 chart
///     起點 = 第一次買入日。</description></item>
/// </list>
/// </para>
/// </summary>
public partial class PortfolioViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAssetChartPeriod5D))]
    [NotifyPropertyChangedFor(nameof(IsAssetChartPeriod1Mo))]
    [NotifyPropertyChangedFor(nameof(IsAssetChartPeriod3Mo))]
    [NotifyPropertyChangedFor(nameof(IsAssetChartPeriod6Mo))]
    [NotifyPropertyChangedFor(nameof(IsAssetChartPeriod1Y))]
    [NotifyPropertyChangedFor(nameof(IsAssetChartPeriod2Y))]
    [NotifyPropertyChangedFor(nameof(IsAssetChartPeriod5Y))]
    [NotifyPropertyChangedFor(nameof(IsAssetChartPeriodMax))]
    private string _assetChartPeriod = "3mo";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAssetChartModePrice))]
    [NotifyPropertyChangedFor(nameof(IsAssetChartModeMyValue))]
    private string _assetChartMode = "price";

    [ObservableProperty] private ISeries[] _assetChartSeries = [];
    [ObservableProperty] private ICartesianAxis[] _assetChartXAxes = [];
    [ObservableProperty] private ICartesianAxis[] _assetChartYAxes = [];
    [ObservableProperty] private bool _isAssetChartLoading;
    [ObservableProperty] private bool _hasAssetChart;

    public bool IsAssetChartPeriod5D => AssetChartPeriod == "5d";
    public bool IsAssetChartPeriod1Mo => AssetChartPeriod == "1mo";
    public bool IsAssetChartPeriod3Mo => AssetChartPeriod == "3mo";
    public bool IsAssetChartPeriod6Mo => AssetChartPeriod == "6mo";
    public bool IsAssetChartPeriod1Y => AssetChartPeriod == "1y";
    public bool IsAssetChartPeriod2Y => AssetChartPeriod == "2y";
    public bool IsAssetChartPeriod5Y => AssetChartPeriod == "5y";
    public bool IsAssetChartPeriodMax => AssetChartPeriod == "max";
    public bool IsAssetChartModePrice => AssetChartMode == "price";
    public bool IsAssetChartModeMyValue => AssetChartMode == "myvalue";

    private CancellationTokenSource? _assetChartCts;

    [RelayCommand]
    private async Task SetAssetChartPeriodAsync(string period)
    {
        if (string.IsNullOrEmpty(period) || period == AssetChartPeriod)
            return;
        AssetChartPeriod = period;
        await LoadAssetChartAsync();
    }

    [RelayCommand]
    private async Task SetAssetChartModeAsync(string mode)
    {
        if (string.IsNullOrEmpty(mode) || mode == AssetChartMode)
            return;
        AssetChartMode = mode;
        await LoadAssetChartAsync();
    }

    /// <summary>
    /// Loads OHLCV history for <see cref="SelectedPositionRow"/> from the
    /// <c>IStockHistoryProvider</c> in the background and rebuilds the series.
    /// Safe to call when no row is selected or no provider was wired (will
    /// silently clear the chart). Concurrent calls cancel the previous load.
    /// </summary>
    internal async Task LoadAssetChartAsync()
    {
        if (_stockHistory is null)
        {
            AssetChartSeries = [];
            HasAssetChart = false;
            return;
        }
        if (SelectedPositionRow is not { } row || string.IsNullOrWhiteSpace(row.Symbol))
        {
            AssetChartSeries = [];
            HasAssetChart = false;
            return;
        }

        _assetChartCts?.Cancel();
        _assetChartCts = new CancellationTokenSource();
        var ct = _assetChartCts.Token;

        IsAssetChartLoading = true;
        try
        {
            var period = ToChartPeriod(AssetChartPeriod);
            var ohlcv = await _stockHistory.GetHistoryAsync(row.Symbol, row.Exchange ?? string.Empty, period, ct);
            if (ct.IsCancellationRequested)
                return;
            BuildAssetChart(row, ohlcv);
        }
        catch (OperationCanceledException)
        {
            // expected when the user switches to another asset before load finishes.
        }
        catch
        {
            // Provider failures collapse the chart — log via snackbar would be too noisy
            // for a passive panel widget. Empty state shows instead.
            AssetChartSeries = [];
            HasAssetChart = false;
        }
        finally
        {
            if (!ct.IsCancellationRequested)
                IsAssetChartLoading = false;
        }
    }

    private static ChartPeriod ToChartPeriod(string period) => period switch
    {
        "5d" => ChartPeriod.FiveDays,
        "1mo" => ChartPeriod.OneMonth,
        "3mo" => ChartPeriod.ThreeMonths,
        "6mo" => ChartPeriod.SixMonths,
        "1y" => ChartPeriod.OneYear,
        "2y" => ChartPeriod.TwoYears,
        "5y" => ChartPeriod.FiveYears,
        "max" => ChartPeriod.Max,
        _ => ChartPeriod.ThreeMonths,
    };

    private void BuildAssetChart(PortfolioRowViewModel row, IReadOnlyList<OhlcvPoint> ohlcv)
    {
        if (ohlcv is null || ohlcv.Count < 2)
        {
            AssetChartSeries = [];
            HasAssetChart = false;
            return;
        }

        // FinMind / Twse 不支援 5D 視窗（最小單位是月）→ provider 退回 1 個月份資料，
        // 這裡用 client-side filter 砍到最近 7 個日曆日（≈ 5 交易日）讓 chip 標籤誠實。
        var filtered = AssetChartPeriod == "5d"
            ? ohlcv.Where(p => p.Date >= DateOnly.FromDateTime(DateTime.Today.AddDays(-7))).ToList()
            : (IReadOnlyList<OhlcvPoint>)ohlcv;
        if (filtered.Count < 2)
        {
            AssetChartSeries = [];
            HasAssetChart = false;
            return;
        }

        // P4.8 — myvalue 模式走 trade journal 重播得到歷史持倉；price 模式直接 close。
        var points = AssetChartMode == "myvalue"
            ? BuildMyValuePoints(filtered, row)
            : BuildPricePoints(filtered);
        if (points.Count < 2)
        {
            AssetChartSeries = [];
            HasAssetChart = false;
            return;
        }

        var accent = GetAssetChartSkColor("AppAccent", "#0078D4");
        var fill = accent.WithAlpha(28);
        var label = GetAssetChartSkColor("AppTextSecondary", "#787B86");
        var sep = GetAssetChartSkColor("AppBorderLight", "#2E2E2E").WithAlpha(56);

        AssetChartSeries =
        [
            new LineSeries<DateTimePoint>
            {
                Values = points,
                Stroke = new SolidColorPaint(accent, 1.5f),
                Fill = new SolidColorPaint(fill),
                GeometrySize = 0,
                GeometryStroke = null,
                LineSmoothness = 0,
                AnimationsSpeed = TimeSpan.Zero,
            }
        ];
        AssetChartXAxes =
        [
            new DateTimeAxis(TimeSpan.FromDays(1), d => d.ToString("MM/dd"))
            {
                TextSize = 9,
                LabelsPaint = new SolidColorPaint(label),
                SeparatorsPaint = new SolidColorPaint(sep),
                TicksPaint = null,
            }
        ];
        AssetChartYAxes =
        [
            new Axis
            {
                Position = LiveChartsCore.Measure.AxisPosition.End,
                TextSize = 9,
                LabelsPaint = new SolidColorPaint(label),
                SeparatorsPaint = new SolidColorPaint(sep),
                TicksPaint = null,
                Labeler = v => v.ToString("N0"),
            }
        ];
        HasAssetChart = true;
    }

    /// <summary>
    /// price 模式：直接把每個 OHLCV close 轉成 (date, value) 點。
    /// </summary>
    private static List<DateTimePoint> BuildPricePoints(IReadOnlyList<OhlcvPoint> ohlcv)
    {
        var points = new List<DateTimePoint>(ohlcv.Count);
        foreach (var p in ohlcv)
            points.Add(new DateTimePoint(p.Date.ToDateTime(TimeOnly.MinValue), (double)p.Close));
        return points;
    }

    /// <summary>
    /// P4.8 myvalue 模式：用 trade journal 重播該標的的歷史持倉，再乘上當天 close。
    /// <list type="bullet">
    ///   <item><description>Buy / StockDividend → qty += t.Quantity</description></item>
    ///   <item><description>Sell → qty -= t.Quantity</description></item>
    ///   <item><description>CashDividend / 其他 → 不變</description></item>
    /// </list>
    /// OHLCV 點 qty=0 時略過（第一筆買入之前，"我的價值" 尚未存在），所以
    /// chart 起點自然落在第一次持有日。Trade event 與 OHLCV 都已排序，
    /// 兩 pointer merge 一遍 = O(n+m)。
    /// </summary>
    private List<DateTimePoint> BuildMyValuePoints(IReadOnlyList<OhlcvPoint> ohlcv, PortfolioRowViewModel row)
    {
        var tradeEvents = Trades
            .Where(t => string.Equals(t.Symbol, row.Symbol, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.TradeDate)
            .Select(t => (
                Date: DateOnly.FromDateTime(t.TradeDate),
                DeltaQty: t.Type switch
                {
                    TradeType.Buy or TradeType.StockDividend => (decimal)t.Quantity,
                    TradeType.Sell => -(decimal)t.Quantity,
                    _ => 0m,
                }))
            .Where(e => e.DeltaQty != 0m)
            .ToList();

        if (tradeEvents.Count == 0)
            return [];

        var points = new List<DateTimePoint>(ohlcv.Count);
        decimal currentQty = 0m;
        int tradeIdx = 0;
        foreach (var p in ohlcv)
        {
            // 套用所有 trade date <= 本日的事件
            while (tradeIdx < tradeEvents.Count && tradeEvents[tradeIdx].Date <= p.Date)
            {
                currentQty += tradeEvents[tradeIdx].DeltaQty;
                tradeIdx++;
            }
            if (currentQty > 0m)
                points.Add(new DateTimePoint(p.Date.ToDateTime(TimeOnly.MinValue), (double)(p.Close * currentQty)));
        }
        return points;
    }

    /// <summary>
    /// Local copy of <c>GetSkColor</c> — <c>PortfolioHistoryViewModel</c> has its
    /// own private helper; we duplicate rather than refactor to keep P4.5
    /// surgical（避免動其他 VM）. 同一個 try-resource fallback pattern.
    /// </summary>
    private static SKColor GetAssetChartSkColor(string key, string hexFallback)
    {
        if (System.Windows.Application.Current?.TryFindResource(key) is System.Windows.Media.SolidColorBrush brush)
        {
            var c = brush.Color;
            return new SKColor(c.R, c.G, c.B, c.A);
        }
        return SKColor.Parse(hexFallback);
    }
}
