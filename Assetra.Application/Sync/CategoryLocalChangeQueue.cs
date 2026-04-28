using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;

namespace Assetra.Application.Sync;

/// <summary>
/// 把 <see cref="SyncOrchestrator"/> 看到的 <see cref="ILocalChangeQueue"/> 介接到實際 Category 倉庫
/// （<see cref="ICategorySyncStore"/>）。v0.20.4 只接 Category 一個 entity，因此本 queue 直接
/// pass-through；未來 Trade/Asset 接上後改用 <c>CompositeLocalChangeQueue</c> 路由 by EntityType。
/// <para>
/// <see cref="RecordManualConflictAsync"/> 暫存於記憶體 list，等 v0.20.5 conflict UI 接上時改寫進
/// 持久化儲存。並提供 <see cref="DrainManualConflicts"/> 給 UI 拉取。
/// </para>
/// </summary>
public sealed class CategoryLocalChangeQueue : ILocalChangeQueue, IManualConflictDrain
{
    private readonly ICategorySyncStore _store;
    private readonly object _lock = new();
    private readonly List<SyncConflict> _manual = new();

    public CategoryLocalChangeQueue(ICategorySyncStore store)
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

    /// <summary>
    /// 取出（並清空）目前累積的 manual conflicts。供 UI 在開啟 conflict 解決面板時呼叫。
    /// </summary>
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
