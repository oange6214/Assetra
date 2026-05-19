namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// A sync store that emits +1 / -1 deltas on its local pending-push counter so
/// <see cref="IGlobalSyncStatusService"/> can keep an in-memory aggregate without
/// polling the database.
///
/// <para>Repositories raise <see cref="LocalChangeCountChanged"/>:
/// <list type="bullet">
///   <item><c>+1</c> after a successful <c>AddAsync</c> / <c>UpdateAsync</c> / <c>RemoveAsync</c>
///     that sets <c>is_pending_push = 1</c>.</item>
///   <item><c>-1</c> after a remote-origin upsert (via <c>ApplyRemoteAsync</c>) that
///     bypasses pending push, when the row WOULD have been counted otherwise. In
///     practice this is rarely needed; the canonical decrement path is the
///     sync service emitting <c>SyncCompleted(pushed)</c> in bulk.</item>
/// </list>
/// </para>
///
/// <para>Subscribers must tolerate out-of-order deltas (the implementation is best-effort,
/// not transactionally aligned with the DB). The startup seed via
/// <see cref="IGlobalSyncStatusService.RefreshAsync"/> is the authoritative reset.</para>
/// </summary>
public interface ILocalChangeCountSource
{
    /// <summary>
    /// Stable identifier for this domain (e.g. <c>"Trade"</c>, <c>"Asset"</c>) — also
    /// the value Cargo / payloads use for <c>EntityType</c>. Used by the status
    /// service to look up per-domain counters.
    /// </summary>
    string SyncDomain { get; }

    /// <summary>Fires after every mutation. Argument is the signed delta (typically +1).</summary>
    event EventHandler<int>? LocalChangeCountChanged;
}
