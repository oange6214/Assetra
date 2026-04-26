namespace Assetra.Core.Models.Import;

public sealed record ImportApplyResult(
    int RowsConsidered,
    int RowsApplied,
    int RowsSkipped,
    int RowsOverwritten,
    IReadOnlyList<string> Warnings);
