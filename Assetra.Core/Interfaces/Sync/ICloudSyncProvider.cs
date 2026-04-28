using Assetra.Core.Models.Sync;

namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// 雲端同步後端的抽象。本介面刻意 stateless：所有 per-device 狀態以 <see cref="SyncMetadata"/> 顯式傳入，
/// provider 不持有「目前裝置」的概念，方便測試與多租戶。
/// <para>
/// v0.20.0 只在 Infrastructure 提供 <c>InMemoryCloudSyncProvider</c> 作為測試 / 開發替身；
/// 真正後端（Supabase / R2 / 自架）留待後續 sprint 評估。
/// </para>
/// </summary>
public interface ICloudSyncProvider
{
    /// <summary>
    /// 拉取自 <paramref name="metadata"/>.Cursor 之後的遠端變更。
    /// </summary>
    Task<SyncPullResult> PullAsync(SyncMetadata metadata, CancellationToken ct = default);

    /// <summary>
    /// 推送本端變更。Provider 內部以 <see cref="EntityVersion.Version"/> +
    /// <see cref="EntityVersion.LastModifiedAt"/> 偵測衝突，被拒的 envelope 會以
    /// <see cref="SyncPushResult.Conflicts"/> 回傳目前 server-side 版本。
    /// </summary>
    Task<SyncPushResult> PushAsync(
        SyncMetadata metadata,
        IReadOnlyList<SyncEnvelope> envelopes,
        CancellationToken ct = default);
}
