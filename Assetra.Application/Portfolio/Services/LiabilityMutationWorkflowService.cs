using Assetra.Application.Loans.Contracts;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Services;

public sealed class LiabilityMutationWorkflowService : ILiabilityMutationWorkflowService
{
    private readonly IAssetRepository _assetRepository;
    private readonly ITradeRepository _tradeRepository;
    private readonly ILoanScheduleRecomputeService? _loanScheduleRecompute;

    public LiabilityMutationWorkflowService(
        IAssetRepository assetRepository,
        ITradeRepository tradeRepository,
        ILoanScheduleRecomputeService? loanScheduleRecompute = null)
    {
        _assetRepository = assetRepository;
        _tradeRepository = tradeRepository;
        _loanScheduleRecompute = loanScheduleRecompute;
    }

    public async Task<LiabilityDeletionResult> DeleteAsync(LiabilityDeletionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (!request.AssetId.HasValue && string.IsNullOrEmpty(request.LoanLabel))
            return new LiabilityDeletionResult(false);

        await _tradeRepository.RemoveByLiabilityAsync(request.AssetId, request.LoanLabel, ct).ConfigureAwait(false);

        if (request.AssetId.HasValue)
            await _assetRepository.DeleteItemAsync(request.AssetId.Value).ConfigureAwait(false);

        return new LiabilityDeletionResult(true);
    }

    public async Task<LiabilityUpdateResult> UpdateAsync(LiabilityUpdateRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var existing = await _assetRepository.GetByIdAsync(request.AssetId).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"找不到負債資產 (Id={request.AssetId})。");
        if (existing.Type != FinancialType.Liability)
            throw new InvalidOperationException("此資產不是負債，無法以負債編輯流程更新。");

        // Build updated record using the with-expression so unspecified fields stay put.
        var updated = existing with
        {
            Name = string.IsNullOrWhiteSpace(request.NewName) ? existing.Name : request.NewName.Trim(),
            IssuerName = request.NewIssuerName ?? existing.IssuerName,
            Subtype = request.NewSubtype ?? existing.Subtype,
            LoanAnnualRate = request.NewAnnualRate ?? existing.LoanAnnualRate,
            LoanTermMonths = request.NewTermMonths ?? existing.LoanTermMonths,
            LoanHandlingFee = request.NewHandlingFee ?? existing.LoanHandlingFee,
            CreditLimit = request.NewCreditLimit ?? existing.CreditLimit,
            BillingDay = request.NewBillingDay ?? existing.BillingDay,
            DueDay = request.NewDueDay ?? existing.DueDay,
            UpdatedAt = DateTime.UtcNow,
        };

        await _assetRepository.UpdateItemAsync(updated).ConfigureAwait(false);

        // Schedule recompute is optional and only applicable when:
        //  * the asset is a loan
        //  * caller explicitly opted in
        //  * rate or term actually changed (avoids no-op recomputes)
        //  * a recompute service is registered (DI may not have wired it in tests)
        var rateChanged = request.NewAnnualRate.HasValue && request.NewAnnualRate.Value != (existing.LoanAnnualRate ?? -1);
        var termChanged = request.NewTermMonths.HasValue && request.NewTermMonths.Value != (existing.LoanTermMonths ?? -1);
        var shouldRecompute = updated.IsLoan
            && request.RecomputeSchedule
            && _loanScheduleRecompute is not null
            && (rateChanged || termChanged)
            && request.OriginalPrincipal.HasValue;

        if (!shouldRecompute)
            return new LiabilityUpdateResult(Success: true, ScheduleRecomputed: false);

        var recomputeResult = await _loanScheduleRecompute!.RecomputeAsync(
            new LoanScheduleRecomputeRequest(
                AssetId: request.AssetId,
                OriginalPrincipal: request.OriginalPrincipal!.Value,
                NewAnnualRate: updated.LoanAnnualRate!.Value,
                NewTermMonths: updated.LoanTermMonths!.Value),
            ct).ConfigureAwait(false);

        return new LiabilityUpdateResult(
            Success: true,
            ScheduleRecomputed: true,
            PreservedPaidCount: recomputeResult.PreservedPaidCount,
            RegeneratedUnpaidCount: recomputeResult.RegeneratedUnpaidCount);
    }
}
