namespace Assetra.Core.Models.Sync;

/// <summary>
/// <see cref="Assetra.Core.Interfaces.Sync.ICloudSyncProvider.PullAsync"/> 的回傳結構：
/// 一批遠端變更 + 新游標。Push 也以類似結構回報結果。
/// </summary>
/// <param name="Envelopes">本批次要回傳給 caller 的變更（可空集合）。</param>
/// <param name="NextCursor">本次同步後的 provider cursor；保存到 <see cref="SyncMetadata.Cursor"/>。</param>
public sealed record SyncPullResult(
    IReadOnlyList<SyncEnvelope> Envelopes,
    string? NextCursor);

/// <summary>
/// <see cref="Assetra.Core.Interfaces.Sync.ICloudSyncProvider.PushAsync"/> 的回傳結構。
/// </summary>
/// <param name="Accepted">遠端已接受並寫入的 envelope ids。</param>
/// <param name="Conflicts">遠端拒絕並回傳目前 server-side 版本，供本端 resolver 處理。</param>
/// <param name="NextCursor">push 後的新游標（部分 provider push 也會推進 cursor）。</param>
public sealed record SyncPushResult(
    IReadOnlyList<Guid> Accepted,
    IReadOnlyList<SyncConflict> Conflicts,
    string? NextCursor);
