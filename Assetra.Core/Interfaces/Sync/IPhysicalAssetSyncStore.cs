using Assetra.Core.Models.Sync;

namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// PhysicalAsset 與雲端同步層之間的介接。
/// 由 <c>PhysicalAssetSqliteRepository</c> 同時實作此介面與 <see cref="MultiAsset.IPhysicalAssetRepository"/>。
/// </summary>
public interface IPhysicalAssetSyncStore
{
    Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default);
    Task MarkPushedAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
    Task ApplyRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default);
}
