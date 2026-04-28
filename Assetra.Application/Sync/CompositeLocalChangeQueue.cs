using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;

namespace Assetra.Application.Sync;

/// <summary>
/// 路由型 <see cref="ILocalChangeQueue"/>：依 <see cref="SyncEnvelope.EntityType"/> 把
/// 操作分派到對應的 entity-specific queue（目前 Category + Trade，未來可擴充 Account / Liability）。
/// 未知 EntityType 的 envelope 會被忽略（log-only 由上層處理；本層保持 idempotent）。
/// </summary>
public sealed class CompositeLocalChangeQueue : ILocalChangeQueue, IManualConflictDrain
{
    private readonly IReadOnlyDictionary<string, ILocalChangeQueue> _queues;

    public CompositeLocalChangeQueue(IReadOnlyDictionary<string, ILocalChangeQueue> queues)
    {
        ArgumentNullException.ThrowIfNull(queues);
        if (queues.Count == 0)
            throw new ArgumentException("At least one queue must be registered.", nameof(queues));
        _queues = queues;
    }

    public IReadOnlyList<SyncConflict> DrainManualConflicts()
    {
        var all = new List<SyncConflict>();
        foreach (var q in _queues.Values)
            if (q is IManualConflictDrain drain)
                all.AddRange(drain.DrainManualConflicts());
        return all;
    }

    public async Task<IReadOnlyList<SyncEnvelope>> GetPendingAsync(CancellationToken ct = default)
    {
        var all = new List<SyncEnvelope>();
        foreach (var q in _queues.Values)
        {
            ct.ThrowIfCancellationRequested();
            all.AddRange(await q.GetPendingAsync(ct).ConfigureAwait(false));
        }
        return all;
    }

    public async Task MarkPushedAsync(IReadOnlyList<Guid> entityIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entityIds);
        if (entityIds.Count == 0) return;
        // entityId alone doesn't carry EntityType, so broadcast to all queues — each store filters by id.
        // Cost is bounded (a couple of UPDATE ... WHERE id = ? per queue).
        foreach (var q in _queues.Values)
        {
            ct.ThrowIfCancellationRequested();
            await q.MarkPushedAsync(entityIds, ct).ConfigureAwait(false);
        }
    }

    public async Task ApplyRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        if (envelopes.Count == 0) return;

        foreach (var group in envelopes.GroupBy(e => e.EntityType))
        {
            ct.ThrowIfCancellationRequested();
            if (!_queues.TryGetValue(group.Key, out var q)) continue;
            await q.ApplyRemoteAsync(group.ToList(), ct).ConfigureAwait(false);
        }
    }

    public async Task RecordManualConflictAsync(IReadOnlyList<SyncConflict> conflicts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        if (conflicts.Count == 0) return;

        foreach (var group in conflicts.GroupBy(c => c.Local.EntityType))
        {
            ct.ThrowIfCancellationRequested();
            if (!_queues.TryGetValue(group.Key, out var q)) continue;
            await q.RecordManualConflictAsync(group.ToList(), ct).ConfigureAwait(false);
        }
    }
}
