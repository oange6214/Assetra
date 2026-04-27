using Assetra.Core.Models.Import;
using Assetra.Core.Models.Reconciliation;

namespace Assetra.Core.Interfaces.Reconciliation;

/// <summary>
/// 對帳作業協調服務：建立 session、跑比對引擎、處置單筆 diff。
/// </summary>
public interface IReconciliationService
{
    /// <summary>
    /// 建立一筆新作業並計算初始 diffs。
    /// 比對範圍：帳戶 = <paramref name="accountId"/> 且日期落在 [<paramref name="periodStart"/>, <paramref name="periodEnd"/>] 的所有 trades vs <paramref name="statementRows"/>。
    /// </summary>
    Task<ReconciliationSession> CreateAsync(
        Guid accountId,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyList<ImportPreviewRow> statementRows,
        Guid? sourceBatchId,
        string? note,
        decimal? statementEndingBalance = null,
        CancellationToken ct = default);

    /// <summary>
    /// 重新比對：以原 statement rows + 當前 trades 重算 diffs，覆寫舊結果（保留 session 標頭）。
    /// 用於使用者完成處置後想看看尚未解決的清單。
    /// </summary>
    Task<IReadOnlyList<ReconciliationDiff>> RecomputeAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// 套用單筆 diff 的處置動作。會驗證 (kind, resolution) 為合法組合，否則拋 <see cref="InvalidOperationException"/>。
    /// 對 <see cref="ReconciliationDiffResolution.Created"/> / <see cref="ReconciliationDiffResolution.Deleted"/> /
    /// <see cref="ReconciliationDiffResolution.OverwrittenFromStatement"/> 會直接動到 trade，
    /// 其餘僅更新 diff 狀態。
    /// </summary>
    Task ApplyResolutionAsync(
        Guid diffId,
        ReconciliationDiffResolution resolution,
        string? note,
        CancellationToken ct = default);

    /// <summary>
    /// v0.10 起：對 <see cref="ReconciliationDiffResolution.Created"/> /
    /// <see cref="ReconciliationDiffResolution.OverwrittenFromStatement"/> 提供完整執行路徑，
    /// 內部透過 <see cref="Assetra.Core.Interfaces.Import.IImportRowApplier"/> 把 statement row 寫入為 trade。
    /// </summary>
    Task ApplyResolutionAsync(
        Guid diffId,
        ReconciliationDiffResolution resolution,
        string? note,
        Assetra.Core.Models.Import.ImportSourceKind? sourceKind,
        Assetra.Core.Models.Import.ImportApplyOptions? options,
        CancellationToken ct = default);

    /// <summary>簽收：所有 diff 均已 Resolved/Ignored 才允許，否則拋例外；session 進入 <see cref="ReconciliationStatus.Resolved"/>。</summary>
    Task SignOffAsync(Guid sessionId, string? note, CancellationToken ct = default);
}
