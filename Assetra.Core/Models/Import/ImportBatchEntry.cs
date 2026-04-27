namespace Assetra.Core.Models.Import;

public enum ImportBatchAction
{
    Added,
    Overwritten,
    Skipped,
}

/// <summary>
/// 單列匯入結果。<see cref="OverwrittenTradeJson"/> 僅在 Overwritten 時非空，
/// 內容為被覆蓋前 <see cref="Trade"/> 的 JSON 序列化結果，供 rollback 還原。
/// </summary>
public sealed record ImportBatchEntry(
    int RowIndex,
    ImportBatchAction Action,
    Guid? NewTradeId,
    string? OverwrittenTradeJson);
