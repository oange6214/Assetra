using Assetra.Core.Models.Sync;

namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// AssetEvent 與雲端同步層的介接（v0.20.10）。子表，需處理父 asset 的 cascade tombstone。
/// 由 <c>AssetSqliteRepository</c> 同時實作此介面與 <see cref="IAssetRepository"/> / <see cref="IAssetSyncStore"/>。
/// </summary>
public interface IAssetEventSyncStore
{
    Task<IReadOnlyList<SyncEnvelope>> GetEventPendingPushAsync(CancellationToken ct = default);
    Task MarkEventPushedAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);
    Task ApplyEventRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default);
}
