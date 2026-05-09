using Assetra.Application.Loans.Contracts;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Core.Services;

namespace Assetra.Application.Loans.Services;

/// <summary>
/// See <see cref="ILoanScheduleRecomputeService"/>. Implementation strategy:
/// <list type="number">
///   <item>Load existing schedule rows for the asset.</item>
///   <item>Delegate to <see cref="AmortizationService.RecomputeUnpaidTail"/>
///         to produce paid-preserved + freshly-amortised rows.</item>
///   <item>Replace the schedule by deleting all existing rows then bulk-inserting
///         the combined list. The repository contract is delete-by-asset +
///         bulk-insert; we re-insert the paid rows verbatim (same Id, TradeId,
///         PaidAt) so historical references stay intact.</item>
/// </list>
/// </summary>
public sealed class LoanScheduleRecomputeService : ILoanScheduleRecomputeService
{
    private readonly ILoanScheduleRepository _scheduleRepo;
    private readonly IAssetRepository _assetRepo;

    public LoanScheduleRecomputeService(
        ILoanScheduleRepository scheduleRepo,
        IAssetRepository assetRepo)
    {
        _scheduleRepo = scheduleRepo ?? throw new ArgumentNullException(nameof(scheduleRepo));
        _assetRepo = assetRepo ?? throw new ArgumentNullException(nameof(assetRepo));
    }

    public async Task<LoanScheduleRecomputeResult> RecomputeAsync(
        LoanScheduleRecomputeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var asset = await _assetRepo.GetByIdAsync(request.AssetId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"找不到負債資產 (Id={request.AssetId})。");
        if (!asset.IsLoan || asset.LoanStartDate is null)
            throw new InvalidOperationException("此負債不是貸款，無法重算攤還表。");

        var existing = await _scheduleRepo.GetByAssetAsync(request.AssetId).ConfigureAwait(false);

        var combined = AmortizationService.RecomputeUnpaidTail(
            assetId:                  request.AssetId,
            originalPrincipal:        request.OriginalPrincipal,
            newAnnualRate:            request.NewAnnualRate,
            newTermMonths:            request.NewTermMonths,
            originalFirstPaymentDate: asset.LoanStartDate.Value,
            existingEntries:          existing);

        // Replace strategy: delete-all then bulk-insert. Paid rows are inserted
        // back verbatim (same Id, TradeId, PaidAt) so their TradeId FK still
        // refers to the original LoanRepay trade row.
        await _scheduleRepo.DeleteByAssetAsync(request.AssetId).ConfigureAwait(false);
        await _scheduleRepo.BulkInsertAsync(combined).ConfigureAwait(false);

        var paidCount = existing.Count(e => e.IsPaid);
        var remainingPrincipal = combined.Where(e => !e.IsPaid).Sum(e => e.PrincipalAmount);
        return new LoanScheduleRecomputeResult(
            PreservedPaidCount:     paidCount,
            RegeneratedUnpaidCount: combined.Count - paidCount,
            RemainingPrincipal:     remainingPrincipal);
    }
}
