using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// 純記憶體 <see cref="ILocalChangeQueue"/>：
/// pending envelopes、已套用的 remote envelopes、manual conflicts 各放一個 list，
/// <see cref="MarkPushedAsync"/> 會從 pending 移除對應 entity。給單元測試 / orchestrator 開發替身。
/// 公開的 list properties 讓測試直接 inspect 結果。
/// </summary>
public sealed class InMemoryLocalChangeQueue : ILocalChangeQueue
{
    private readonly object _lock = new();
    private readonly List<SyncEnvelope> _pending = new();

    public List<SyncEnvelope> AppliedRemotes { get; } = new();
    public List<SyncConflict> ManualConflicts { get; } = new();

    public InMemoryLocalChangeQueue() { }

    public InMemoryLocalChangeQueue(IEnumerable<SyncEnvelope> initialPending)
    {
        ArgumentNullException.ThrowIfNull(initialPending);
        _pending.AddRange(initialPending);
    }

    public Task<IReadOnlyList<SyncEnvelope>> GetPendingAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
            return Task.FromResult<IReadOnlyList<SyncEnvelope>>(_pending.ToList());
    }

    public Task MarkPushedAsync(IReadOnlyList<Guid> entityIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entityIds);
        ct.ThrowIfCancellationRequested();
        var idSet = entityIds.ToHashSet();
        lock (_lock)
            _pending.RemoveAll(e => idSet.Contains(e.EntityId));
        return Task.CompletedTask;
    }

    public Task ApplyRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
            AppliedRemotes.AddRange(envelopes);
        return Task.CompletedTask;
    }

    public Task RecordManualConflictAsync(IReadOnlyList<SyncConflict> conflicts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conflicts);
        ct.ThrowIfCancellationRequested();
        lock (_lock)
            ManualConflicts.AddRange(conflicts);
        return Task.CompletedTask;
    }

    public void EnqueuePending(SyncEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        lock (_lock) _pending.Add(envelope);
    }
}
