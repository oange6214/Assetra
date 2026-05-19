using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

/// <summary>
/// Historical FX rate store. Supports point-in-time lookups for any past date
/// + bulk ingest from external sources (Yahoo, central bank, manual upload).
///
/// <para>Unlike the existing <see cref="IFxRateProvider"/> (which returns the
/// latest rate), this repository is purely historical and is meant to back
/// multi-currency reporting aggregations where a fixed as-of date matters.</para>
/// </summary>
public interface IFxRateHistoryRepository
{
    /// <summary>
    /// Exact-date lookup. Returns null when no row exists for the given key.
    /// Use <see cref="GetNearestAsync"/> for "last available business day" semantics.
    /// </summary>
    Task<FxRateHistoryEntry?> GetAsync(
        DateOnly date, string baseCcy, string quoteCcy, CancellationToken ct = default);

    /// <summary>
    /// Nearest-date lookup — returns the most recent entry on or before <paramref name="date"/>
    /// within the lookback window (default 7 days). Useful for queries on
    /// weekends / holidays when the FX market is closed.
    /// </summary>
    Task<FxRateHistoryEntry?> GetNearestAsync(
        DateOnly date, string baseCcy, string quoteCcy, int lookbackDays = 7, CancellationToken ct = default);

    /// <summary>
    /// Bulk-write multiple rows. Idempotent — duplicates of (date, base, quote) are
    /// overwritten in place, preserving the latest <see cref="FxRateHistoryEntry.IngestedAt"/>.
    /// </summary>
    Task UpsertRangeAsync(
        IReadOnlyCollection<FxRateHistoryEntry> entries, CancellationToken ct = default);

    /// <summary>
    /// All rows for a (from, to) pair within an inclusive date range, ordered by
    /// Date asc. Used by trend/return calculators to project a base-currency time series.
    /// </summary>
    Task<IReadOnlyList<FxRateHistoryEntry>> GetRangeAsync(
        string baseCcy, string quoteCcy, DateOnly from, DateOnly to, CancellationToken ct = default);
}
