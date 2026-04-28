using Assetra.Core.Models.Sync;

namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// RealEstate 與雲端同步層之間的介接。
/// 由 <c>RealEstateSqliteRepository</c> 同時實作此介面與 <see cref="MultiAsset.IRealEstateRepository"/>。
/// </summary>
public interface IRealEstateSyncStore
{
    Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default);
    Task MarkPushedAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
    Task ApplyRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default);
}
