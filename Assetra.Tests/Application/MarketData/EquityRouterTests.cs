using Assetra.Application.MarketData;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Application.MarketData;

public class EquityRouterTests
{
    [Fact]
    public async Task GetQuotesAsync_FallsBackToLaterProviderWhenEarlierProviderMisses()
    {
        var key = new EquityInstrumentKey("2330", "TWSE");
        var quote = Quote(key, 910m, "Official");
        var router = new EquityRouter(
        [
            new FakeProvider(
                "Fugle",
                key.Exchange,
                MarketDataResult<EquityQuote>.Failure(new MarketDataError(
                    MarketDataErrorCode.ProviderUnavailable,
                    "Fugle unavailable",
                    Provider: "Fugle",
                    Instrument: key,
                    IsRetryable: true))),
            new FakeProvider("Official", key.Exchange, MarketDataResult<EquityQuote>.Success(quote)),
        ]);

        var results = await router.GetQuotesAsync([key]);

        var result = Assert.Single(results);
        Assert.True(result.IsSuccess);
        Assert.Equal("Official", result.Value?.SourceProvider);
    }

    [Fact]
    public async Task GetQuotesAsync_ReturnsUnsupportedWhenNoProviderCanHandle()
    {
        var key = new EquityInstrumentKey("AAPL", "NASDAQ");
        var router = new EquityRouter([new FakeProvider("TWSE", "TWSE")]);

        var result = Assert.Single(await router.GetQuotesAsync([key]));

        Assert.False(result.IsSuccess);
        Assert.Equal(MarketDataErrorCode.UnsupportedSymbol, result.Error?.Code);
        Assert.Equal(key, result.Error?.Instrument);
    }

    [Fact]
    public async Task GetQuotesAsync_PreservesFirstOccurrenceOrderAfterDistinct()
    {
        var twseKey = new EquityInstrumentKey("2330", "TWSE");
        var tpexKey = new EquityInstrumentKey("6488", "TPEX");
        var router = new EquityRouter(
        [
            new FakeProvider("TWSE", "TWSE", MarketDataResult<EquityQuote>.Success(Quote(twseKey, 100m, "TWSE"))),
            new FakeProvider("TPEX", "TPEX", MarketDataResult<EquityQuote>.Success(Quote(tpexKey, 200m, "TPEX"))),
        ]);

        var results = await router.GetQuotesAsync([twseKey, tpexKey, twseKey]);

        Assert.Collection(
            results,
            r => Assert.Equal(twseKey, r.Value?.Instrument),
            r => Assert.Equal(tpexKey, r.Value?.Instrument));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(120)]
    public async Task GetQuoteAsync_ReusesCachedQuoteWithinCallerMaxAge(int ttlSeconds)
    {
        var key = new EquityInstrumentKey("AAPL", "NASDAQ");
        var cache = new InMemoryEquityQuoteCache();
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-08T14:30:00Z"));
        var provider = new CountingProvider("Twelve Data", key.Exchange) { Price = 100m };
        var router = new EquityRouter([provider], cache, clock);

        var first = await router.GetQuoteAsync(key, TimeSpan.FromSeconds(ttlSeconds));
        provider.Price = 200m;
        clock.Advance(TimeSpan.FromSeconds(ttlSeconds - 1));
        var second = await router.GetQuoteAsync(key, TimeSpan.FromSeconds(ttlSeconds));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(100m, second.Value?.Price);
        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task GetQuoteAsync_ZeroMaxAgeBypassesCacheAndRefreshesValue()
    {
        var key = new EquityInstrumentKey("AAPL", "NASDAQ");
        var cache = new InMemoryEquityQuoteCache();
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-08T14:30:00Z"));
        var provider = new CountingProvider("Twelve Data", key.Exchange) { Price = 100m };
        var router = new EquityRouter([provider], cache, clock);

        var first = await router.GetQuoteAsync(key, EquityQuoteCachePolicies.Fresh);
        provider.Price = 200m;
        var second = await router.GetQuoteAsync(key, EquityQuoteCachePolicies.Fresh);

        Assert.Equal(100m, first.Value?.Price);
        Assert.Equal(200m, second.Value?.Price);
        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task GetQuoteAsync_ExpiredCacheFetchesProviderAgain()
    {
        var key = new EquityInstrumentKey("AAPL", "NASDAQ");
        var cache = new InMemoryEquityQuoteCache();
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-08T14:30:00Z"));
        var provider = new CountingProvider("Twelve Data", key.Exchange) { Price = 100m };
        var router = new EquityRouter([provider], cache, clock);

        _ = await router.GetQuoteAsync(key, TimeSpan.FromSeconds(30));
        provider.Price = 200m;
        clock.Advance(TimeSpan.FromSeconds(31));
        var refreshed = await router.GetQuoteAsync(key, TimeSpan.FromSeconds(30));

        Assert.Equal(200m, refreshed.Value?.Price);
        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task GetQuoteAsync_ReturnsStaleCachedQuoteWhenRefreshProviderFails()
    {
        var key = new EquityInstrumentKey("AAPL", "NASDAQ");
        var cache = new InMemoryEquityQuoteCache();
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-08T14:30:00Z"));
        var seedingProvider = new CountingProvider("Twelve Data", key.Exchange) { Price = 100m };
        var seedingRouter = new EquityRouter([seedingProvider], cache, clock);
        _ = await seedingRouter.GetQuoteAsync(key, EquityQuoteCachePolicies.Fresh);

        clock.Advance(TimeSpan.FromMinutes(5));
        var error = new MarketDataError(
            MarketDataErrorCode.NetworkFailure,
            "Twelve Data temporarily unavailable",
            Provider: "Twelve Data",
            Instrument: key,
            IsRetryable: true);
        var failingRouter = new EquityRouter(
            [new FakeProvider("Twelve Data", key.Exchange, MarketDataResult<EquityQuote>.Failure(error))],
            cache,
            clock);

        var stale = await failingRouter.GetQuoteAsync(key, TimeSpan.FromSeconds(30));

        Assert.True(stale.IsSuccess);
        Assert.Equal(100m, stale.Value?.Price);
        Assert.True(stale.Value?.IsStale);
        Assert.Equal(error.Message, stale.Value?.ProviderStateMessage);
    }

    [Fact]
    public async Task GetQuoteAsync_DoesNotMaskMissingApiKeyWithStaleCache()
    {
        var key = new EquityInstrumentKey("AAPL", "NASDAQ");
        var cache = new InMemoryEquityQuoteCache();
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-08T14:30:00Z"));
        var seedingProvider = new CountingProvider("Twelve Data", key.Exchange) { Price = 100m };
        var seedingRouter = new EquityRouter([seedingProvider], cache, clock);
        _ = await seedingRouter.GetQuoteAsync(key, EquityQuoteCachePolicies.Fresh);

        clock.Advance(TimeSpan.FromMinutes(5));
        var error = new MarketDataError(
            MarketDataErrorCode.MissingApiKey,
            "Twelve Data API key is missing",
            Provider: "Twelve Data",
            Instrument: key);
        var missingKeyRouter = new EquityRouter(
            [new FakeProvider("Twelve Data", key.Exchange, MarketDataResult<EquityQuote>.Failure(error))],
            cache,
            clock);

        var result = await missingKeyRouter.GetQuoteAsync(key, TimeSpan.FromSeconds(30));

        Assert.False(result.IsSuccess);
        Assert.Equal(MarketDataErrorCode.MissingApiKey, result.Error?.Code);
    }

    [Fact]
    public async Task GetQuoteAsync_CacheKeyDoesNotIncludeProvider()
    {
        var key = new EquityInstrumentKey("AAPL", "NASDAQ");
        var cache = new InMemoryEquityQuoteCache();
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-05-08T14:30:00Z"));
        var firstProvider = new CountingProvider("Twelve Data", key.Exchange) { Price = 100m };
        var firstRouter = new EquityRouter([firstProvider], cache, clock);
        _ = await firstRouter.GetQuoteAsync(key, EquityQuoteCachePolicies.Fresh);

        var secondProvider = new CountingProvider("Future Provider", key.Exchange) { Price = 200m };
        var secondRouter = new EquityRouter([secondProvider], cache, clock);
        var cached = await secondRouter.GetQuoteAsync(key, EquityQuoteCachePolicies.HoverPreview);

        Assert.True(cached.IsSuccess);
        Assert.Equal("Twelve Data", cached.Value?.SourceProvider);
        Assert.Equal(100m, cached.Value?.Price);
        Assert.Equal(0, secondProvider.CallCount);
    }

    private static EquityQuote Quote(EquityInstrumentKey key, decimal price, string source) =>
        new(
            key,
            price,
            previousClose: price - 1m,
            change: 1m,
            changePercent: 1m,
            currency: StockExchangeRegistry.ResolveDefaultCurrency(key.Exchange),
            updatedAt: DateTimeOffset.UnixEpoch,
            sourceProvider: source,
            isDelayed: false);

    private sealed class FakeProvider(
        string providerName,
        string exchange,
        MarketDataResult<EquityQuote>? result = null) : IEquityQuoteProvider
    {
        public string ProviderName => providerName;

        public bool CanHandle(EquityInstrumentKey key) =>
            string.Equals(key.Exchange, exchange, StringComparison.OrdinalIgnoreCase);

        public Task<MarketDataResult<EquityQuote>> GetQuoteAsync(
            EquityInstrumentKey key,
            CancellationToken ct = default)
        {
            return Task.FromResult(result ?? MarketDataResult<EquityQuote>.Failure(new MarketDataError(
                MarketDataErrorCode.UnsupportedSymbol,
                "Unsupported",
                Provider: providerName,
                Instrument: key)));
        }

        public Task<IReadOnlyList<MarketDataResult<EquityQuote>>> GetQuotesAsync(
            IReadOnlyList<EquityInstrumentKey> keys,
            CancellationToken ct = default)
        {
            IReadOnlyList<MarketDataResult<EquityQuote>> results = keys
                .Select(k => result ?? MarketDataResult<EquityQuote>.Failure(new MarketDataError(
                    MarketDataErrorCode.UnsupportedSymbol,
                    "Unsupported",
                    Provider: providerName,
                    Instrument: k)))
                .ToList();
            return Task.FromResult(results);
        }
    }

    private sealed class CountingProvider(string providerName, string exchange) : IEquityQuoteProvider
    {
        public string ProviderName => providerName;
        public int CallCount { get; private set; }
        public decimal Price { get; set; }

        public bool CanHandle(EquityInstrumentKey key) =>
            string.Equals(key.Exchange, exchange, StringComparison.OrdinalIgnoreCase);

        public async Task<MarketDataResult<EquityQuote>> GetQuoteAsync(
            EquityInstrumentKey key,
            CancellationToken ct = default)
        {
            var results = await GetQuotesAsync([key], ct);
            return results.Single();
        }

        public Task<IReadOnlyList<MarketDataResult<EquityQuote>>> GetQuotesAsync(
            IReadOnlyList<EquityInstrumentKey> keys,
            CancellationToken ct = default)
        {
            CallCount++;
            IReadOnlyList<MarketDataResult<EquityQuote>> results = keys
                .Select(k => MarketDataResult<EquityQuote>.Success(Quote(k, Price, providerName)))
                .ToList();
            return Task.FromResult(results);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan value) => _now += value;
    }
}
