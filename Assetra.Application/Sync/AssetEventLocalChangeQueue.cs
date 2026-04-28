using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;

namespace Assetra.Application.Sync;

/// <summary>
/// 把 <see cref="IAssetEventSyncStore"/> 包成 <see cref="ILocalChangeQueue"/>。
/// 子表（asset_event）依賴父 asset 的 cascade tombstone 由 store 處理。
/// </summary>
public sealed class AssetEventLocalChangeQueue : ILocalChangeQueue, IManualConflictDrain
{
    private readonly IAssetEventSyncStore _store;
    private readonly object _lock = new();
    private readonly List<SyncConflict> _manual = new();

    public AssetEventLocalChangeQueue(IAssetEventSyncStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public Task<IReadOnlyList<SyncEnvelope>> GetPendingAsync(CancellationToken ct = default)
        => _store.GetEventPendingPushAsync(ct);

    public Task MarkPushedAsync(IReadOnlyList<Guid> entityIds, CancellationToken ct = default)
        => _store.MarkEventPushedAsync(entityIds, ct);

    public Task ApplyRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default)
        => _store.ApplyEventRemoteAsync(envelopes, ct);

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
