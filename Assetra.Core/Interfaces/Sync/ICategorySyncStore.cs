using Assetra.Core.Models.Sync;

namespace Assetra.Core.Interfaces.Sync;

/// <summary>
/// Category 與雲端同步層之間的介接：把待 push 的 envelope 取出、push 完清除 outbox flag、
/// 把遠端拉到的 envelope 寫進本地。
/// <para>
/// 本介面只暴露 sync-shaped 操作（不公開 <c>ExpenseCategory</c> 直接 mutation），
/// 既有 <see cref="ICategoryRepository"/> 是給 ViewModel 的 user-facing API，
/// 兩者由同一份 SQLite 實作（<c>CategorySqliteRepository</c>）共同實現。
/// </para>
/// <para>
/// 當未來新增其他可同步 entity（Trade / Asset / …）時，每個都會有對應的
/// <c>I{Entity}SyncStore</c>，再由一個 <c>CompositeLocalChangeQueue</c> 路由到正確 store。
/// v0.20.4 只接 Category，因此 <c>CategoryLocalChangeQueue</c> 直接 wrap 此 store。
/// </para>
/// </summary>
public interface ICategorySyncStore
{
    /// <summary>
    /// 取出所有 <c>is_pending_push = 1</c> 的 row（含 tombstone），轉成 envelope。
    /// </summary>
    Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default);

    /// <summary>
    /// 把指定 ids 的 <c>is_pending_push</c> 清為 0。代表這些 envelope 已成功 push。
    /// </summary>
    Task MarkPushedAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);

    /// <summary>
    /// 把遠端拉回的 envelopes 寫進本地（upsert）。
    /// 若本地有同 id 但 <c>version</c> ≥ envelope.Version，跳過（防止 backwards write）。
    /// 寫入時 <c>is_pending_push = 0</c>（remote-origin 不需再 push）。
    /// </summary>
    Task ApplyRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default);
}
