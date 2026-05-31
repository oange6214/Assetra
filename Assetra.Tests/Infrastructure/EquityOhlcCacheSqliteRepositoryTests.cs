using System.IO;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.History;
using Assetra.Infrastructure.Persistence;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public sealed class EquityOhlcCacheSqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"assetra-ohlc-cache-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        try
        { File.Delete(_dbPath); }
        catch { /* best effort */ }
    }

    [Fact]
    public async Task UpsertManyAsync_DoesNotUseProviderAsUniqueKey()
    {
        var repo = new EquityOhlcCacheSqliteRepository(_dbPath);
        var date = new DateOnly(2026, 5, 8);

        await repo.UpsertManyAsync([
            Entry("AAPL", "NASDAQ", date, close: 100m, provider: "twelvedata"),
        ]);
        await repo.UpsertManyAsync([
            Entry("aapl", "nasdaq", date, close: 101m, provider: "finnhub"),
        ]);

        var rows = await repo.GetRangeAsync("AAPL", "NASDAQ", "1d", date, date);

        Assert.Single(rows);
        Assert.Equal(101m, rows[0].Candle.Close);
        Assert.Equal("finnhub", rows[0].SourceProvider);
    }

    [Fact]
    public async Task CachedProvider_ReturnsFreshCacheWithoutCallingInnerProvider()
    {
        var repo = new EquityOhlcCacheSqliteRepository(_dbPath);
        var now = new DateTimeOffset(2026, 5, 10, 8, 0, 0, TimeSpan.Zero);
        await repo.UpsertManyAsync([
            Entry("AAPL", "NASDAQ", new DateOnly(2026, 5, 8), close: 188m, provider: "twelvedata", now),
        ]);
        var inner = new FakeHistoryProvider([]);
        var sut = new CachedStockHistoryProvider(inner, repo, new FixedTimeProvider(now));

        var rows = await sut.GetHistoryAsync("AAPL", "NASDAQ", ChartPeriod.OneMonth);

        Assert.Single(rows);
        Assert.Equal(188m, rows[0].Close);
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task CachedProvider_PersistsFetchedCandlesWhenCacheIsMissing()
    {
        var repo = new EquityOhlcCacheSqliteRepository(_dbPath);
        var now = new DateTimeOffset(2026, 5, 10, 8, 0, 0, TimeSpan.Zero);
        var fetched = new[]
        {
            new OhlcvPoint(new DateOnly(2026, 5, 7), 10m, 12m, 9m, 11m, 1000),
            new OhlcvPoint(new DateOnly(2026, 5, 8), 11m, 13m, 10m, 12m, 1200),
        };
        var sut = new CachedStockHistoryProvider(
            new FakeHistoryProvider(fetched),
            repo,
            new FixedTimeProvider(now));

        var rows = await sut.GetHistoryAsync("AAPL", "NASDAQ", ChartPeriod.OneMonth);
        var cached = await repo.GetRangeAsync(
            "AAPL",
            "NASDAQ",
            "1d",
            new DateOnly(2026, 5, 7),
            new DateOnly(2026, 5, 8));

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, cached.Count);
        Assert.All(cached, row => Assert.Equal("dynamic-history", row.SourceProvider));
    }

    private static EquityOhlcCacheEntry Entry(
        string symbol,
        string exchange,
        DateOnly date,
        decimal close,
        string provider,
        DateTimeOffset? updatedAt = null) =>
        new(
            symbol,
            exchange,
            "1d",
            new OhlcvPoint(date, close, close, close, close, 100),
            StockExchangeRegistry.ResolveDefaultCurrency(exchange),
            provider,
            updatedAt ?? new DateTimeOffset(2026, 5, 8, 8, 0, 0, TimeSpan.Zero),
            IsAdjusted: false);

    private sealed class FakeHistoryProvider(IReadOnlyList<OhlcvPoint> points) : IStockHistoryProvider
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<OhlcvPoint>> GetHistoryAsync(
            string symbol,
            string exchange,
            ChartPeriod period,
            CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(points);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
