using Assetra.Core.Models.Sync;

namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// AssetItem 與雲端同步層之間的介接，鏡 <see cref="ITradeSyncStore"/> 的契約。
/// 由 <c>AssetSqliteRepository</c> 同時實作此介面與 <see cref="IAssetRepository"/>。
/// AssetGroup / AssetEvent 在 v0.20.8 暫不同步。
/// </summary>
public interface IAssetSyncStore
{
    Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default);
    Task MarkPushedAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
    Task ApplyRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default);
}
