namespace Assetra.Application.Loans.Contracts;

/// <summary>
/// Recomputes the amortisation schedule of an existing loan after the user
/// edits its annual rate or term. Already-paid periods are preserved verbatim;
/// only the unpaid tail is regenerated against the new parameters.
///
/// <para>
/// This is the second-half of the loan-edit story: after
/// <c>ILiabilityMutationWorkflowService.UpdateAsync</c> persists the new
/// <see cref="Assetra.Core.Models.AssetItem.LoanAnnualRate"/> /
/// <see cref="Assetra.Core.Models.AssetItem.LoanTermMonths"/>, callers invoke
/// this service to bring the schedule rows in line. The two are intentionally
/// split so callers can update metadata without touching the schedule
/// (e.g. rename only).
/// </para>
/// </summary>
public interface ILoanScheduleRecomputeService
{
    Task<LoanScheduleRecomputeResult> RecomputeAsync(LoanScheduleRecomputeRequest request, CancellationToken ct = default);
}

/// <summary>
/// <paramref name="OriginalPrincipal"/> = the borrowed amount captured when the
/// loan was first created (LoanBorrow trade's <c>CashAmount</c>). The service
/// uses it to compute remaining principal as
/// <c>OriginalPrincipal − Σ paid PrincipalAmount</c>.
/// </summary>
public sealed record LoanScheduleRecomputeRequest(
    Guid AssetId,
    decimal OriginalPrincipal,
    decimal NewAnnualRate,
    int NewTermMonths);

public sealed record LoanScheduleRecomputeResult(
    int PreservedPaidCount,
    int RegeneratedUnpaidCount,
    decimal RemainingPrincipal);
