using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IEquityOhlcCacheRepository
{
    Task UpsertManyAsync(IReadOnlyList<EquityOhlcCacheEntry> candles, CancellationToken ct = default);

    Task<IReadOnlyList<EquityOhlcCacheEntry>> GetRangeAsync(
        string symbol,
        string exchange,
        string interval,
        DateOnly start,
        DateOnly end,
        CancellationToken ct = default);
}
