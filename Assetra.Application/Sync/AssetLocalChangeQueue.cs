using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;

namespace Assetra.Application.Sync;

/// <summary>
/// 把 <see cref="IAssetSyncStore"/> 包成 <see cref="ILocalChangeQueue"/>，
/// 與 <see cref="TradeLocalChangeQueue"/> 對稱。Manual conflict 暫存於記憶體 list。
/// 線上實際路由由 <see cref="CompositeLocalChangeQueue"/> 依 <c>EntityType</c> 分派。
/// </summary>
public sealed class AssetLocalChangeQueue : ILocalChangeQueue, IManualConflictDrain
{
    private readonly IAssetSyncStore _store;
    private readonly object _lock = new();
    private readonly List<SyncConflict> _manual = new();

    public AssetLocalChangeQueue(IAssetSyncStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public Task<IReadOnlyList<SyncEnvelope>> GetPendingAsync(CancellationToken ct = default)
        => _store.GetPendingPushAsync(ct);

    public Task MarkPushedAsync(IReadOnlyList<Guid> entityIds, CancellationToken ct = default)
        => _store.MarkPushedAsync(entityIds, ct);

    public Task ApplyRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default)
        => _store.ApplyRemoteAsync(envelopes, ct);

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
