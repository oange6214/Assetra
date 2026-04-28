using Assetra.Core.Models.Import;
using Assetra.Core.Models.Reconciliation;

namespace Assetra.Core.Interfaces.Reconciliation;

/// <summary>對帳作業 (session) + 對應 diffs 的儲存介面。Diffs 與 session 為 1:N，刪除 session 連動刪除 diffs。</summary>
public interface IReconciliationSessionRepository
{
    Task<IReadOnlyList<ReconciliationSession>> GetAllAsync(CancellationToken ct = default);

    Task<ReconciliationSession?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(
        ReconciliationSession session,
        IReadOnlyList<ImportPreviewRow> statementRows,
        IReadOnlyList<ReconciliationDiff> diffs,
        CancellationToken ct = default);

    /// <summary>取出 session 建立時的原始對帳單列（recompute 用）。</summary>
    Task<IReadOnlyList<ImportPreviewRow>> GetStatementRowsAsync(Guid sessionId, CancellationToken ct = default);

    Task UpdateStatusAsync(Guid id, ReconciliationStatus status, string? note, CancellationToken ct = default);

    Task RemoveAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ReconciliationDiff>> GetDiffsAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>單筆 diff 查詢；查無資料回傳 <see langword="null"/>。</summary>
    Task<ReconciliationDiff?> GetDiffByIdAsync(Guid diffId, CancellationToken ct = default);

    /// <summary>覆寫 session 的所有 diffs（重新比對時使用）。</summary>
    Task ReplaceDiffsAsync(Guid sessionId, IReadOnlyList<ReconciliationDiff> diffs, CancellationToken ct = default);

    Task UpdateDiffResolutionAsync(
        Guid diffId,
        ReconciliationDiffResolution resolution,
        DateTimeOffset? resolvedAt,
        string? note,
        CancellationToken ct = default);
}
