using Assetra.Core.Models.Sync;

namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// RetirementAccount 與雲端同步層之間的介接。
/// 由 <c>RetirementAccountSqliteRepository</c> 同時實作此介面與 <see cref="MultiAsset.IRetirementAccountRepository"/>。
/// </summary>
public interface IRetirementAccountSyncStore
{
    Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default);
    Task MarkPushedAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
    Task ApplyRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default);
}
