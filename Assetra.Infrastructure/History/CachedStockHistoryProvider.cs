using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.History;

internal sealed class CachedStockHistoryProvider(
    IStockHistoryProvider inner,
    IEquityOhlcCacheRepository cache,
    TimeProvider timeProvider,
    string sourceProvider = "dynamic-history") : IStockHistoryProvider
{
    private const string DailyInterval = "1d";
    private static readonly TimeSpan MutableRefreshAge = TimeSpan.FromHours(12);
    private static readonly TimeSpan CacheCoverageGrace = TimeSpan.FromDays(7);

    public async Task<IReadOnlyList<OhlcvPoint>> GetHistoryAsync(
        string symbol,
        string exchange,
        ChartPeriod period,
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var (start, end) = ResolveRange(period, DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime));

        var cached = await cache
            .GetRangeAsync(symbol, exchange, DailyInterval, start, end, ct)
            .ConfigureAwait(false);

        if (!ShouldFetch(cached, start, end, now))
            return cached.Select(c => c.Candle).ToList();

        var fetched = await inner.GetHistoryAsync(symbol, exchange, period, ct).ConfigureAwait(false);
        if (fetched.Count == 0)
            return cached.Select(c => c.Candle).ToList();

        var key = new EquityInstrumentKey(symbol, exchange);
        var currency = StockExchangeRegistry.ResolveDefaultCurrency(key.Exchange);
        var entries = fetched
            .OrderBy(p => p.Date)
            .Select(p => new EquityOhlcCacheEntry(
                key.Symbol,
                key.Exchange,
                DailyInterval,
                p,
                currency,
                sourceProvider,
                now,
                IsAdjusted: false))
            .ToList();

        await cache.UpsertManyAsync(entries, ct).ConfigureAwait(false);
        return entries.Select(e => e.Candle).ToList();
    }

    private static bool ShouldFetch(
        IReadOnlyList<EquityOhlcCacheEntry> cached,
        DateOnly start,
        DateOnly end,
        DateTimeOffset now)
    {
        if (cached.Count == 0)
            return true;

        var newestSourceUpdate = cached.Max(c => c.SourceUpdatedAt);
        if (now - newestSourceUpdate < MutableRefreshAge)
            return false;

        var first = cached[0].Candle.Date;
        var last = cached[^1].Candle.Date;
        if (first > start.AddDays((int)CacheCoverageGrace.TotalDays))
            return true;
        if (last < end.AddDays(-(int)CacheCoverageGrace.TotalDays))
            return true;

        return last >= end.AddDays(-3);
    }

    private static (DateOnly Start, DateOnly End) ResolveRange(ChartPeriod period, DateOnly today)
    {
        var start = period switch
        {
            ChartPeriod.OneMonth => today.AddMonths(-1),
            ChartPeriod.ThreeMonths => today.AddMonths(-3),
            ChartPeriod.OneYear => today.AddYears(-1),
            ChartPeriod.TwoYears => today.AddYears(-2),
            _ => today.AddMonths(-3),
        };

        return (start, today);
    }
}
