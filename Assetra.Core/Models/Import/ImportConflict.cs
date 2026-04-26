namespace Assetra.Core.Models.Import;

public enum ImportConflictResolution
{
    Skip,
    Overwrite,
    AddAnyway,
}

/// <summary>
/// 預覽時偵測到的去重衝突：<see cref="Row"/> 與資料庫中既有資料的 dedupe hash 相同。
/// </summary>
public sealed record ImportConflict(
    ImportPreviewRow Row,
    Guid? ExistingTradeId,
    Guid? ExistingTransactionId,
    ImportConflictResolution Resolution = ImportConflictResolution.Skip);
