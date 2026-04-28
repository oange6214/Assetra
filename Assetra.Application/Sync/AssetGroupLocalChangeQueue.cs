using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;

namespace Assetra.Application.Sync;

/// <summary>
/// 把 <see cref="IAssetGroupSyncStore"/> 包成 <see cref="ILocalChangeQueue"/>。
/// 線上實際路由由 <see cref="CompositeLocalChangeQueue"/> 依 <c>EntityType</c> 分派。
/// </summary>
public sealed class AssetGroupLocalChangeQueue : ILocalChangeQueue, IManualConflictDrain
{
    private readonly IAssetGroupSyncStore _store;
    private readonly object _lock = new();
    private readonly List<SyncConflict> _manual = new();

    public AssetGroupLocalChangeQueue(IAssetGroupSyncStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public Task<IReadOnlyList<SyncEnvelope>> GetPendingAsync(CancellationToken ct = default)
        => _store.GetGroupPendingPushAsync(ct);

    public Task MarkPushedAsync(IReadOnlyList<Guid> entityIds, CancellationToken ct = default)
        => _store.MarkGroupPushedAsync(entityIds, ct);

    public Task ApplyRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default)
        => _store.ApplyGroupRemoteAsync(envelopes, ct);

    public Task RecordManualConflictAsync(IReadOnlyList<SyncConflict> conflicts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        ct.ThrowIfCancellationRequested();
        lock (_lock) _manual.AddRange(conflicts);
        return Task.CompletedTask;
    }

    public IReadOnlyList<SyncConflict> DrainManualConflicts()
    {
        lock (_lock)
        {
            var copy = _manual.ToList();
            _manual.Clear();
            return copy;
        }
    }
}
