using System.Collections.Concurrent;
using Assetra.Core.Interfaces;

namespace Assetra.Application.Fx;

/// <summary>
/// Application-layer FX history accessor. Wraps the repo with:
/// <list type="bullet">
///   <item>Per-(date, from, to) in-memory cache so repeated lookups during a
///     single report render don't re-query SQLite. 5-minute TTL bounds memory growth.</item>
///   <item>Nearest-date fallback semantics — exact match misses fall through to
///     the most recent rate within a 7-day window.</item>
///   <item>Same-currency short-circuit returning 1.0 without touching the repo.</item>
/// </list>
/// </summary>
public sealed class FxRateHistoryService : IFxRateHistoryService
{
    private readonly IFxRateHistoryRepository _repo;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private sealed record CacheEntry(decimal? Rate, DateTimeOffset CachedAt);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    public FxRateHistoryService(IFxRateHistoryRepository repo)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
    }

    public async Task<decimal?> GetRateAsync(
        DateOnly date, string fromCurrency, string toCurrency, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fromCurrency) || string.IsNullOrWhiteSpace(toCurrency))
            return null;
        if (string.Equals(fromCurrency, toCurrency, StringComparison.OrdinalIgnoreCase))
            return 1m;

        var key = CacheKey(date, fromCurrency, toCurrency);
        if (_cache.TryGetValue(key, out var cached)
            && DateTimeOffset.UtcNow - cached.CachedAt < CacheTtl)
        {
            return cached.Rate;
        }

        // Try exact match first, then fall back to nearest preceding business day.
        var exact = await _repo.GetAsync(date, fromCurrency, toCurrency, ct).ConfigureAwait(false);
        decimal? rate = exact?.Rate;
        if (rate is null)
        {
            var nearest = await _repo.GetNearestAsync(date, fromCurrency, toCurrency, lookbackDays: 7, ct).ConfigureAwait(false);
            rate = nearest?.Rate;
        }

        _cache[key] = new CacheEntry(rate, DateTimeOffset.UtcNow);
        return rate;
    }

    private static string CacheKey(DateOnly date, string from, string to)
        => $"{date:yyyy-MM-dd}|{from.ToUpperInvariant()}|{to.ToUpperInvariant()}";
}
