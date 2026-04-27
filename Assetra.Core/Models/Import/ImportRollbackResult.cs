namespace Assetra.Core.Models.Import;

public sealed record ImportRollbackFailure(int RowIndex, string Reason);

public sealed record ImportRollbackResult(
    Guid HistoryId,
    int Reverted,
    int Restored,
    int Skipped,
    IReadOnlyList<ImportRollbackFailure> Failures)
{
    public bool IsFullyReverted => Failures.Count == 0;
}
