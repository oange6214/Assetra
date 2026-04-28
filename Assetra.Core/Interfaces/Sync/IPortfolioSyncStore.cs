using Assetra.Core.Models.Sync;

namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// PortfolioEntry 與雲端同步層之間的介接，鏡 <see cref="ITradeSyncStore"/> 的契約。
/// 由 <c>PortfolioSqliteRepository</c> 同時實作此介面與 <see cref="IPortfolioRepository"/>。
/// </summary>
public interface IPortfolioSyncStore
{
    Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default);
    Task MarkPushedAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
    Task ApplyRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default);
}
