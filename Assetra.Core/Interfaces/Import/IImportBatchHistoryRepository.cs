using Assetra.Core.Models.Import;

namespace Assetra.Core.Interfaces.Import;

public interface IImportBatchHistoryRepository
{
    /// <summary>寫入 batch 留底（含所有 entries）。</summary>
    Task SaveAsync(ImportBatchHistory history, CancellationToken ct = default);

    /// <summary>
    /// 取得最近 <paramref name="limit"/> 筆 history（依 AppliedAt DESC），
    /// 為了列表效能，<see cref="ImportBatchHistory.Entries"/> 為空集合。
    /// </summary>
    Task<IReadOnlyList<ImportBatchHistory>> GetRecentAsync(int limit, CancellationToken ct = default);

    /// <summary>取得單筆 history，含完整 entries（rollback 用）。</summary>
    Task<ImportBatchHistory?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>標記為已 rollback 並記錄時間，不刪除資料。</summary>
    Task MarkRolledBackAsync(Guid id, DateTimeOffset rolledBackAt, CancellationToken ct = default);

    /// <summary>
    /// 反查指定 history 的原始 <see cref="ImportPreviewRow"/> 列表（v0.9+）。
    /// 僅回傳保留有 <c>preview_row_json</c> 的列；舊資料可能為空。
    /// 主要供 Reconciliation context 以既有 batch 為對帳來源時使用。
    /// </summary>
    Task<IReadOnlyList<ImportPreviewRow>> GetPreviewRowsAsync(Guid historyId, CancellationToken ct = default);
}
