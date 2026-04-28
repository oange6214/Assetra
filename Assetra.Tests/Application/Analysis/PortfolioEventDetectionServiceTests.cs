using Assetra.Application.Analysis;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Application.Analysis;

public class PortfolioEventDetectionServiceTests
{
    private static Trade Buy(string symbol, DateTime date, decimal price, int qty) =>
        new(Guid.NewGuid(), symbol, "TWSE", symbol, TradeType.Buy, date, price, qty, null, null);

    private static Trade Sell(string symbol, DateTime date, decimal price, int qty) =>
        new(Guid.NewGuid(), symbol, "TWSE", symbol, TradeType.Sell, date, price, qty, 0m, 0m);

    private static Trade Dividend(string symbol, DateTime date, decimal cash) =>
        new(Guid.NewGuid(), symbol, "TWSE", symbol, TradeType.CashDividend, date, 1m, 1, null, null,
            CashAmount: cash);

    [Fact]
    public void Detect_EmptyTrades_ReturnsEmpty()
    {
        var events = PortfolioEventDetectionService.Detect(Array.Empty<Trade>());
        Assert.Empty(events);
    }

    [Fact]
    public void Detect_BuyAboveThreshold_EmitsLargeTradeEvent()
    {
        var trades = new[] { Buy("2330", new DateTime(2026, 1, 5), 600m, 200) }; // 120_000

        var events = PortfolioEventDetectionService.Detect(trades, largeTradeThreshold: 100_000m);

        var ev = Assert.Single(events);
        Assert.Equal(PortfolioEventKind.LargeTrade, ev.Kind);
        Assert.Equal("2330", ev.Symbol);
        Assert.Equal(120_000m, ev.Amount);
        Assert.Equal(new DateOnly(2026, 1, 5), ev.Date);
    }

    [Fact]
    public void Detect_BuyBelowThreshold_NoEvent()
    {
        var trades = new[] { Buy("2330", new DateTime(2026, 1, 5), 600m, 100) }; // 60_000

        var events = PortfolioEventDetectionService.Detect(trades, largeTradeThreshold: 100_000m);

        Assert.Empty(events);
    }

    [Fact]
    public void Detect_SellAboveThreshold_AlsoEmitted()
    {
        var trades = new[] { Sell("2330", new DateTime(2026, 1, 5), 600m, 200) };

        var events = PortfolioEventDetectionService.Detect(trades, largeTradeThreshold: 100_000m);

        var ev = Assert.Single(events);
        Assert.Equal(PortfolioEventKind.LargeTrade, ev.Kind);
        Assert.Contains("Sell", ev.Label);
    }

    [Fact]
    public void Detect_FirstCashDividend_EmitsFirstDividendEvent()
    {
        var trades = new[]
        {
            Dividend("2330", new DateTime(2026, 7, 1), 5_000m),
            Dividend("2330", new DateTime(2026, 10, 1), 5_000m),
        };

        var events = PortfolioEventDetectionService.Detect(trades);
        var first = Assert.Single(events.Where(e => e.Kind == PortfolioEventKind.FirstDividend));
        Assert.Equal("2330", first.Symbol);
        Assert.Equal(new DateOnly(2026, 7, 1), first.Date);
    }

    [Fact]
    public void Detect_MultipleSymbols_EachEmitsOwnFirstDividend()
    {
        var trades = new[]
        {
            Dividend("2330", new DateTime(2026, 7, 1), 5_000m),
            Dividend("2317", new DateTime(2026, 8, 1), 3_000m),
            Dividend("2330", new DateTime(2026, 10, 1), 5_000m),
        };

        var events = PortfolioEventDetectionService.Detect(trades);
        var firstDivs = events.Where(e => e.Kind == PortfolioEventKind.FirstDividend).ToList();

        Assert.Equal(2, firstDivs.Count);
        Assert.Contains(firstDivs, e => e.Symbol == "2330");
        Assert.Contains(firstDivs, e => e.Symbol == "2317");
    }

    [Fact]
    public void Detect_EventsAreOrderedByDate()
    {
        var trades = new[]
        {
            Buy("2330", new DateTime(2026, 3, 1), 600m, 200),    // large
            Buy("2317", new DateTime(2026, 1, 15), 200m, 1000),  // large, earlier
        };

        var events = PortfolioEventDetectionService.Detect(trades);

        Assert.Equal(2, events.Count);
        Assert.True(events[0].Date < events[1].Date);
    }

    [Fact]
    public void Detect_NegativeThreshold_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PortfolioEventDetectionService.Detect(Array.Empty<Trade>(), -1m));
    }

    [Fact]
    public void Detect_NullTrades_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PortfolioEventDetectionService.Detect(null!));
    }
}
