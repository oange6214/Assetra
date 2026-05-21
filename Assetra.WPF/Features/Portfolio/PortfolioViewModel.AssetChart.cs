using System.Threading;
using System.Threading.Tasks;
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
/// price OHLCV，繪成 LiveCharts line series。時間視窗對應既有
/// <see cref="ChartPeriod"/> enum (1月 / 3月 / 1年 / 2年)。可切換顯示模式：
/// <list type="bullet">
///   <item><description><b>price</b>：純 close price 走勢</description></item>
///   <item><description><b>myvalue</b>：close × <c>SelectedPositionRow.Quantity</c>，
///     展示「我這部位的市值走勢」。注意：以**當前**持倉數量乘上歷史價，
///     非歷史持倉重播（為 MVP 簡化；要精準回放需走 trade journal）。</description></item>
/// </list>
/// </para>
/// </summary>
public partial class PortfolioViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAssetChartPeriod1Mo))]
    [NotifyPropertyChangedFor(nameof(IsAssetChartPeriod3Mo))]
    [NotifyPropertyChangedFor(nameof(IsAssetChartPeriod1Y))]
    [NotifyPropertyChangedFor(nameof(IsAssetChartPeriod2Y))]
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

    public bool IsAssetChartPeriod1Mo => AssetChartPeriod == "1mo";
    public bool IsAssetChartPeriod3Mo => AssetChartPeriod == "3mo";
    public bool IsAssetChartPeriod1Y => AssetChartPeriod == "1y";
    public bool IsAssetChartPeriod2Y => AssetChartPeriod == "2y";
    public bool IsAssetChartModePrice => AssetChartMode == "price";
    public bool IsAssetChartModeMyValue => AssetChartMode == "myvalue";

    private CancellationTokenSource? _assetChartCts;

    [RelayCommand]
    private async Task SetAssetChartPeriodAsync(string period)
    {
        if (string.IsNullOrEmpty(period) || period == AssetChartPeriod) return;
        AssetChartPeriod = period;
        await LoadAssetChartAsync();
    }

    [RelayCommand]
    private async Task SetAssetChartModeAsync(string mode)
    {
        if (string.IsNullOrEmpty(mode) || mode == AssetChartMode) return;
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
            if (ct.IsCancellationRequested) return;
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
        "1mo" => ChartPeriod.OneMonth,
        "3mo" => ChartPeriod.ThreeMonths,
        "1y" => ChartPeriod.OneYear,
        "2y" => ChartPeriod.TwoYears,
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

        var multiplier = AssetChartMode == "myvalue" ? row.Quantity : 1m;
        var points = new List<DateTimePoint>(ohlcv.Count);
        foreach (var p in ohlcv)
            points.Add(new DateTimePoint(p.Date.ToDateTime(TimeOnly.MinValue), (double)(p.Close * multiplier)));

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
