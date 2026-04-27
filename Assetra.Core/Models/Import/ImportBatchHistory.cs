namespace Assetra.Core.Models.Import;

/// <summary>
/// 一筆匯入 batch 套用後的留底，作為 v0.8 rollback 與審計依據。
/// </summary>
public sealed record ImportBatchHistory(
    Guid Id,
    Guid BatchId,
    string FileName,
    ImportFormat Format,
    DateTimeOffset AppliedAt,
    int RowsApplied,
    int RowsSkipped,
    int RowsOverwritten,
    bool IsRolledBack,
    DateTimeOffset? RolledBackAt,
    IReadOnlyList<ImportBatchEntry> Entries);
