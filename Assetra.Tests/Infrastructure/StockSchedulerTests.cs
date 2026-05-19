using Microsoft.Reactive.Testing;
using Moq;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.Scheduling;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class StockSchedulerTests
{
    // Empty portfolio repo — used across all tests that don't need portfolio data
    private static Mock<IPortfolioRepository> EmptyPortfolio()
    {
        var mock = new Mock<IPortfolioRepository>();
        mock.Setup(r => r.GetEntriesAsync()).ReturnsAsync([]);
        return mock;
    }

    private static Mock<IAlertRepository> Alerts(params AlertRule[] rules)
    {
        var mock = new Mock<IAlertRepository>();
        mock.Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rules);
        return mock;
    }

    [Fact]
    public void Start_EmitsQuotesImmediately()
    {
        var scheduler = new TestScheduler();
        var router = new FakeRouter();

        // Assetra StockScheduler fetches from portfolio, not watchlist
        EmptyPortfolio().Setup(r => r.GetEntriesAsync()).ReturnsAsync([]);

        var svc = new StockScheduler(
            router,
            EmptyPortfolio().Object,
            Alerts().Object,
            scheduler,
            TimeSpan.FromSeconds(10));

        IReadOnlyList<StockQuote>? received = null;
        svc.QuoteStream.Subscribe(q => received = q);
        svc.Start();

        scheduler.AdvanceBy(1); // trigger immediate
        // Empty portfolio → no symbols → no quotes emitted, but subscription fires
        Assert.NotNull(received);
    }

    [Fact]
    public void Stop_HaltsPolling()
    {
        var scheduler = new TestScheduler();
        var router = new FakeRouter();

        var svc = new StockScheduler(
            router,
            EmptyPortfolio().Object,
            Alerts().Object,
            scheduler,
            TimeSpan.FromSeconds(10));

        svc.Start();
        scheduler.AdvanceBy(1);
        svc.Stop();
        scheduler.AdvanceBy(TimeSpan.FromSeconds(30).Ticks);

        // Empty portfolio → guard skips fetch; polling halted so no subsequent calls
        Assert.Equal(0, router.CallCount);
    }

    [Fact]
    public void Start_FetchesQuotesForActiveAlertsWithoutPortfolioPosition()
    {
        var scheduler = new TestScheduler();
        var quote = new EquityQuote(
            new EquityInstrumentKey("2330", "TWSE"),
            910m,
            895m,
            15m,
            1.68m,
            "TWD",
            DateTimeOffset.UnixEpoch,
            "Test",
            isDelayed: false,
            "台積電");
        var router = new FakeRouter(MarketDataResult<EquityQuote>.Success(quote));
        var alerts = Alerts(new AlertRule(
            Guid.NewGuid(),
            "2330",
            "TWSE",
            AlertCondition.Above,
            900m));

        var svc = new StockScheduler(
            router,
            EmptyPortfolio().Object,
            alerts.Object,
            scheduler,
            TimeSpan.FromSeconds(10));

        IReadOnlyList<StockQuote>? received = null;
        svc.QuoteStream.Subscribe(q => received = q);
        svc.Start();

        scheduler.AdvanceBy(1);

        Assert.NotNull(received);
        Assert.Contains(received, q => q.Symbol == "2330" && q.Exchange == "TWSE");
        Assert.Equal(1, router.CallCount);
        Assert.Contains(router.LastKeys, k => k.Symbol == "2330" && k.Exchange == "TWSE");
    }

    [Fact]
    public void Start_SkipsSymbolsClosedByTradingCalendar_AfterFirstSeenTick()
    {
        // Behavior contract (updated for off-hours bypass):
        //   1. First tick: cold rows ALWAYS get through, even when the calendar
        //      says closed. This is the UX fix — Taiwan users should see US
        //      last-close prices after the app boots overnight.
        //   2. Second tick onwards: rows already seen respect the calendar gate
        //      (so we don't burn API quota refreshing a static last-close every 10s).
        var scheduler = new TestScheduler();
        // Two successful results so first tick can serve cold AAPL.
        var aaplQuote = new EquityQuote(
            new EquityInstrumentKey("AAPL", "NASDAQ"),
            210m, 200m, 10m, 5m, "USD", DateTimeOffset.UnixEpoch,
            "Test", isDelayed: true, "Apple");
        var router = new FakeRouter(
            MarketDataResult<EquityQuote>.Success(aaplQuote));
        var alerts = Alerts(new AlertRule(
            Guid.NewGuid(),
            "AAPL",
            "NASDAQ",
            AlertCondition.Above,
            200m));

        var svc = new StockScheduler(
            router,
            EmptyPortfolio().Object,
            alerts.Object,
            scheduler,
            TimeSpan.FromSeconds(10),
            calendar: new ClosedTradingCalendar());

        IReadOnlyList<StockQuote>? received = null;
        svc.QuoteStream.Subscribe(q => received = q);
        svc.Start();

        // First fetch — cold-row bypass lets AAPL through.
        scheduler.AdvanceBy(1);
        Assert.NotNull(received);
        Assert.Contains(received, q => q.Symbol == "AAPL");
        Assert.Equal(1, router.CallCount);

        // Second fetch (10s later) — AAPL is now seen, calendar gate applies, no call.
        scheduler.AdvanceBy(TimeSpan.FromSeconds(10).Ticks);
        Assert.Equal(1, router.CallCount);
    }

    [Fact]
    public void Start_FetchesTaiwanQuotesWhenTradingCalendarIsClosed()
    {
        var scheduler = new TestScheduler();
        var twseQuote = new EquityQuote(
            new EquityInstrumentKey("2330", "TWSE"),
            910m,
            895m,
            15m,
            1.68m,
            "TWD",
            DateTimeOffset.UnixEpoch,
            "Test",
            isDelayed: true,
            "台積電");
        var tpexQuote = new EquityQuote(
            new EquityInstrumentKey("6488", "TPEX"),
            45m,
            44m,
            1m,
            2.27m,
            "TWD",
            DateTimeOffset.UnixEpoch,
            "Test",
            isDelayed: true,
            "環球晶");
        var router = new FakeRouter(
            MarketDataResult<EquityQuote>.Success(twseQuote),
            MarketDataResult<EquityQuote>.Success(tpexQuote));
        var alerts = Alerts(
            new AlertRule(Guid.NewGuid(), "2330", "TWSE", AlertCondition.Above, 900m),
            new AlertRule(Guid.NewGuid(), "6488", "TPEX", AlertCondition.Above, 40m),
            new AlertRule(Guid.NewGuid(), "AAPL", "NASDAQ", AlertCondition.Above, 200m));

        var svc = new StockScheduler(
            router,
            EmptyPortfolio().Object,
            alerts.Object,
            scheduler,
            TimeSpan.FromSeconds(10),
            calendar: new ClosedTradingCalendar());

        IReadOnlyList<StockQuote>? received = null;
        svc.QuoteStream.Subscribe(q => received = q);
        svc.Start();

        scheduler.AdvanceBy(1);

        Assert.NotNull(received);
        Assert.Contains(received, q => q.Symbol == "2330" && q.Exchange == "TWSE");
        Assert.Contains(received, q => q.Symbol == "6488" && q.Exchange == "TPEX");
        Assert.Equal(1, router.CallCount);
        Assert.Contains(router.LastKeys, k => k.Symbol == "2330" && k.Exchange == "TWSE");
        Assert.Contains(router.LastKeys, k => k.Symbol == "6488" && k.Exchange == "TPEX");
        // AAPL passes the cold-row bypass on first fetch — calendar gate only kicks
        // in on subsequent fetches (verified by Start_SkipsSymbolsClosedByTradingCalendar_AfterFirstSeenTick).
        Assert.Contains(router.LastKeys, k => k.Symbol == "AAPL" && k.Exchange == "NASDAQ");
    }

    private sealed class FakeRouter(params MarketDataResult<EquityQuote>[] results) : IEquityRouter
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<EquityInstrumentKey> LastKeys { get; private set; } = [];

        public Task<MarketDataResult<EquityQuote>> GetQuoteAsync(
            EquityInstrumentKey key,
            CancellationToken ct = default)
        {
            return GetQuoteAsync(key, EquityQuoteCachePolicies.Fresh, ct);
        }

        public Task<MarketDataResult<EquityQuote>> GetQuoteAsync(
            EquityInstrumentKey key,
            TimeSpan maxAge,
            CancellationToken ct = default)
        {
            return Task.FromResult(results.FirstOrDefault()
                ?? MarketDataResult<EquityQuote>.Failure(new MarketDataError(
                    MarketDataErrorCode.UnsupportedSymbol,
                    "No quote",
                    Instrument: key)));
        }

        public Task<IReadOnlyList<MarketDataResult<EquityQuote>>> GetQuotesAsync(
            IReadOnlyList<EquityInstrumentKey> keys,
            CancellationToken ct = default)
        {
            return GetQuotesAsync(keys, EquityQuoteCachePolicies.Fresh, ct);
        }

        public Task<IReadOnlyList<MarketDataResult<EquityQuote>>> GetQuotesAsync(
            IReadOnlyList<EquityInstrumentKey> keys,
            TimeSpan maxAge,
            CancellationToken ct = default)
        {
            CallCount++;
            LastKeys = keys;
            IReadOnlyList<MarketDataResult<EquityQuote>> response = results.Length > 0 ? results : [];
            return Task.FromResult(response);
        }
    }

    private sealed class ClosedTradingCalendar : ITradingCalendarService
    {
        public TradingDayKind GetTradingDayKind(string exchange, DateOnly localDate) => TradingDayKind.Holiday;

        public bool ShouldRefreshQuotes(string exchange, DateTimeOffset utcNow) => false;
    }
}
