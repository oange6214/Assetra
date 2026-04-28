using Assetra.Core.Models.Sync;

namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// AssetGroup 與雲端同步層的介接（v0.20.10）。
/// 由 <c>AssetSqliteRepository</c> 同時實作此介面與 <see cref="IAssetRepository"/> / <see cref="IAssetSyncStore"/>。
/// 系統 group（IsSystem=1）由 migrator seed，不會被 mutation 路徑碰到，因此也不會 push。
/// </summary>
public interface IAssetGroupSyncStore
{
    Task<IReadOnlyList<SyncEnvelope>> GetGroupPendingPushAsync(CancellationToken ct = default);
    Task MarkGroupPushedAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
    Task ApplyGroupRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default);
}
