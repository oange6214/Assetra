namespace Assetra.Core.Models.Import;

public enum ImportBatchAction
{
    Added,
    Overwritten,
    Skipped,
}

/// <summary>
/// 單列匯入結果。
/// <para>
/// <see cref="OverwrittenTradeJson"/> 僅在 Overwritten 時非空，內容為被覆蓋前 <see cref="Trade"/> 的 JSON 序列化結果，供 rollback 還原。
/// </para>
/// <para>
/// <see cref="PreviewRowJson"/> 為原始 <see cref="ImportPreviewRow"/> 的 JSON 快照（v0.9+），
/// 保留以供 Reconciliation context 反查匯入來源資料；舊資料可能為 <c>null</c>。
/// </para>
/// </summary>
public sealed record ImportBatchEntry(
    int RowIndex,
    ImportBatchAction Action,
    Guid? NewTradeId,
    string? OverwrittenTradeJson,
    string? PreviewRowJson = null);
