using Assetra.Core.Models;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Assetra.WPF.Features.Portfolio;

public sealed class PortfolioGroupDetailViewModel
{
    public PortfolioGroupDetailViewModel(
        Guid groupId,
        string name,
        string currency,
        IReadOnlyList<PortfolioRowViewModel> holdings,
        IReadOnlyList<TradeRowViewModel>? trades = null)
    {
        GroupId = groupId;
        Name = name;
        Currency = string.IsNullOrWhiteSpace(currency) ? "TWD" : currency;
        Holdings = holdings;
        Trades = trades ?? [];

        MarketValue = holdings.Sum(row => DisplayAmount(row.MarketValue, row.MarketValueBase));
        Cost = holdings.Sum(row => DisplayAmount(row.Cost, row.CostBase));
        Pnl = holdings.Sum(row => DisplayAmount(row.Pnl, row.PnlBase));
        TodayChange = holdings.Sum(row => DisplayAmount(row.DayChange, 0m));
        IsPnlPositive = Pnl >= 0m;
        IsTodayChangePositive = TodayChange >= 0m;
        MarketValueTrendValues = BuildMarketValueTrend(holdings);
        IsMarketValueTrendPositive = MarketValueTrendValues.Count < 2 ||
                                     MarketValueTrendValues[^1] >= MarketValueTrendValues[0];
        CostTrendValues = BuildCostTrend(MarketValueTrendValues.Count, Cost);
        PnlTrendValues = BuildPnlTrend(MarketValueTrendValues, CostTrendValues);
        (MarketValueTrendSeries, MarketValueTrendXAxes, MarketValueTrendYAxes) =
            BuildMarketValueTrendChart(MarketValueTrendValues, IsMarketValueTrendPositive);
        (PerformanceTrendSeries, PerformanceTrendXAxes, PerformanceTrendYAxes) =
            BuildPerformanceTrendChart(MarketValueTrendValues, CostTrendValues);
    }

    public Guid GroupId { get; }
    public string Name { get; }
    public string Currency { get; }
    public IReadOnlyList<PortfolioRowViewModel> Holdings { get; }
    public IReadOnlyList<TradeRowViewModel> Trades { get; }
    public int HoldingCount => Holdings.Count;
    public int TradeCount => Trades.Count;
    public bool HasHoldings => HoldingCount > 0;
    public bool HasTrades => TradeCount > 0;
    public decimal MarketValue { get; }
    public decimal Cost { get; }
    public decimal Pnl { get; }
    public decimal TodayChange { get; }
    public bool IsPnlPositive { get; }
    public bool IsTodayChangePositive { get; }
    public IReadOnlyList<double> MarketValueTrendValues { get; }
    public bool HasMarketValueTrend => MarketValueTrendValues.Count >= 2;
    public IReadOnlyList<double> CostTrendValues { get; }
    public IReadOnlyList<double> PnlTrendValues { get; }
    public bool HasPerformanceTrend =>
        MarketValueTrendValues.Count >= 2 &&
        CostTrendValues.Count == MarketValueTrendValues.Count &&
        PnlTrendValues.Count == MarketValueTrendValues.Count;
    public bool IsMarketValueTrendPositive { get; }
    public ISeries[] MarketValueTrendSeries { get; }
    public ICartesianAxis[] MarketValueTrendXAxes { get; }
    public ICartesianAxis[] MarketValueTrendYAxes { get; }
    public ISeries[] PerformanceTrendSeries { get; }
    public ICartesianAxis[] PerformanceTrendXAxes { get; }
    public ICartesianAxis[] PerformanceTrendYAxes { get; }

    public Money MarketValueAsMoney => new(MarketValue, Currency);
    public Money CostAsMoney => new(Cost, Currency);
    public Money PnlAsMoney => new(Pnl, Currency);
    public Money TodayChangeAsMoney => new(TodayChange, Currency);

    private static decimal DisplayAmount(decimal nativeAmount, decimal baseAmount) =>
        baseAmount != 0m ? baseAmount : nativeAmount;

    /// <summary>Task 1.4 — exposed for PortfolioViewModel.SelectedPortfolioHeader.</summary>
    internal static IReadOnlyList<double> BuildMarketValueTrendPublic(IReadOnlyList<PortfolioRowViewModel> holdings)
        => BuildMarketValueTrend(holdings);

    /// <summary>Task 1.4 — exposed for PortfolioViewModel.SelectedPortfolioHeader.</summary>
    internal static (ISeries[] Series, ICartesianAxis[] XAxes, ICartesianAxis[] YAxes)
        BuildMarketValueTrendChartPublic(IReadOnlyList<double> values, bool isPositive)
        => BuildMarketValueTrendChart(values, isPositive);

    private static IReadOnlyList<double> BuildMarketValueTrend(IReadOnlyList<PortfolioRowViewModel> holdings)
    {
        var sources = new List<(double[] Points, double Quantity, double BaseFactor)>();
        foreach (var row in holdings)
        {
            if (row.SparklinePoints is not { Length: >= 2 } points || row.Quantity <= 0m)
                continue;

            sources.Add((points, (double)row.Quantity, ResolveBaseFactor(row)));
        }

        if (sources.Count == 0)
            return [];

        var length = sources.Min(source => source.Points.Length);
        if (length < 2)
            return [];

        var values = new double[length];
        foreach (var source in sources)
        {
            var start = source.Points.Length - length;
            for (var i = 0; i < length; i++)
                values[i] += source.Points[start + i] * source.Quantity * source.BaseFactor;
        }

        return values;
    }

    private static IReadOnlyList<double> BuildCostTrend(int length, decimal cost)
    {
        if (length < 2)
            return [];

        return Enumerable.Repeat((double)cost, length).ToArray();
    }

    private static IReadOnlyList<double> BuildPnlTrend(
        IReadOnlyList<double> marketValues,
        IReadOnlyList<double> costs)
    {
        if (marketValues.Count < 2 || costs.Count != marketValues.Count)
            return [];

        var values = new double[marketValues.Count];
        for (var i = 0; i < values.Length; i++)
            values[i] = marketValues[i] - costs[i];

        return values;
    }

    private static double ResolveBaseFactor(PortfolioRowViewModel row)
    {
        if (row.MarketValue > 0m && row.MarketValueBase > 0m)
            return (double)(row.MarketValueBase / row.MarketValue);

        if (row.CurrentPrice > 0m && row.CurrentPriceBase > 0m)
            return (double)(row.CurrentPriceBase / row.CurrentPrice);

        return 1d;
    }

    private static (ISeries[] Series, ICartesianAxis[] XAxes, ICartesianAxis[] YAxes)
        BuildMarketValueTrendChart(IReadOnlyList<double> values, bool isPositive)
    {
        if (values.Count < 2)
            return ([], [], []);

        var stroke = isPositive
            ? GetSkColor("AppUp", "#D13438")
            : GetSkColor("AppDown", "#107C10");
        var fill = stroke.WithAlpha(24);

        return (
            [
                new LineSeries<double>
                {
                    Values = values,
                    Stroke = new SolidColorPaint(stroke, 1.5f),
                    Fill = new SolidColorPaint(fill),
                    GeometrySize = 0,
                    GeometryStroke = null,
                    LineSmoothness = 0,
                    AnimationsSpeed = TimeSpan.Zero,
                }
            ],
            [new Axis { IsVisible = false }],
            [new Axis { IsVisible = false }]);
    }

    private static (ISeries[] Series, ICartesianAxis[] XAxes, ICartesianAxis[] YAxes)
        BuildPerformanceTrendChart(
            IReadOnlyList<double> marketValues,
            IReadOnlyList<double> costs)
    {
        if (marketValues.Count < 2 || costs.Count != marketValues.Count)
            return ([], [], []);

        var marketStroke = GetSkColor("AppAccent", "#0F6CBD");
        var costStroke = GetSkColor("AppTextMuted", "#64748B");

        return (
            [
                new LineSeries<double>
                {
                    Values = marketValues,
                    Stroke = new SolidColorPaint(marketStroke, 1.8f),
                    Fill = new SolidColorPaint(marketStroke.WithAlpha(18)),
                    GeometrySize = 0,
                    GeometryStroke = null,
                    LineSmoothness = 0,
                    AnimationsSpeed = TimeSpan.Zero,
                },
                new LineSeries<double>
                {
                    Values = costs,
                    Stroke = new SolidColorPaint(costStroke, 1.2f),
                    Fill = null,
                    GeometrySize = 0,
                    GeometryStroke = null,
                    LineSmoothness = 0,
                    AnimationsSpeed = TimeSpan.Zero,
                },
            ],
            [new Axis { IsVisible = false }],
            [new Axis { IsVisible = false }]);
    }

    private static SKColor GetSkColor(string key, string hexFallback)
    {
        if (System.Windows.Application.Current?.TryFindResource(key) is System.Windows.Media.SolidColorBrush brush)
        {
            var c = brush.Color;
            return new SKColor(c.R, c.G, c.B, c.A);
        }

        return SKColor.Parse(hexFallback);
    }
}
