namespace Assetra.Core.Models.Import;

public enum ImportBatchStatus
{
    Draft,
    Previewing,
    Confirmed,
    Applied,
    Cancelled,
}

/// <summary>
/// 一次匯入會話：使用者上傳的檔案、偵測到的格式、以及解析出的預覽列與衝突。
/// </summary>
public sealed record ImportBatch(
    Guid Id,
    string FileName,
    ImportFileType FileType,
    ImportFormat Format,
    DateTimeOffset CreatedAt,
    IReadOnlyList<ImportPreviewRow> Rows,
    IReadOnlyList<ImportConflict> Conflicts,
    ImportBatchStatus Status = ImportBatchStatus.Draft)
{
    public ImportSourceKind SourceKind => Format.ToSourceKind();
    public int RowCount => Rows.Count;
    public int ConflictCount => Conflicts.Count;
    public int NewRowCount => RowCount - ConflictCount;
}
