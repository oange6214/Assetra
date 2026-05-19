namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// Counts <c>is_pending_push = 1</c> rows across every sync-enabled table in a
/// single call. Used by <see cref="IGlobalSyncStatusService"/> to refresh the
/// aggregate snapshot on a debounced timer + immediately after each sync tick.
///
/// <para>Implementation runs ~13 short <c>SELECT COUNT(*)</c> queries against
/// the SQLite WAL connection; total cost is sub-millisecond on a normal DB
/// so calling once per 5 seconds is negligible.</para>
/// </summary>
public interface IPendingPushCounter
{
    /// <summary>
    /// Returns a per-domain dictionary of pending push counts. Keys are stable
    /// domain identifiers (e.g. <c>"Trade"</c>, <c>"Asset"</c>). Missing keys
    /// mean the domain has no sync infrastructure yet (Goal, PortfolioGroup
    /// at time of writing).
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> CountPendingByDomainAsync(CancellationToken ct = default);
}
