using Assetra.Core.Models.Sync;

namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// AutoCategorizationRule 與雲端同步層的介接（v0.20.11）。
/// 由 <c>AutoCategorizationRuleSqliteRepository</c> 同時實作此介面與
/// <see cref="IAutoCategorizationRuleRepository"/>。
/// </summary>
public interface IAutoCategorizationRuleSyncStore
{
    Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default);
    Task MarkPushedAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
    Task ApplyRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default);
}
