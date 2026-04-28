using Assetra.Core.Models.Sync;

namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// RecurringTransaction 與雲端同步層的介接（v0.20.11）。
/// 由 <c>RecurringTransactionSqliteRepository</c> 同時實作此介面與
/// <see cref="IRecurringTransactionRepository"/>。
/// PendingRecurringEntry 是本地 materialized queue，不同步。
/// </summary>
public interface IRecurringTransactionSyncStore
{
    Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default);
    Task MarkPushedAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
    Task ApplyRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default);
}
