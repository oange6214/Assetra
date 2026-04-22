using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Core.Services;

namespace Assetra.Application.Portfolio.Services;

public sealed class LoanMutationWorkflowService : ILoanMutationWorkflowService
{
    private readonly IAssetRepository _assetRepository;
    private readonly ILoanScheduleRepository _loanScheduleRepository;
    private readonly ITransactionService _transactionService;

    public LoanMutationWorkflowService(
        IAssetRepository assetRepository,
        ILoanScheduleRepository loanScheduleRepository,
        ITransactionService transactionService)
    {
        _assetRepository = assetRepository;
        _loanScheduleRepository = loanScheduleRepository;
        _transactionService = transactionService;
    }

    public async Task<LoanMutationResult> RecordAsync(
        LoanTransactionRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        AssetItem? liabilityAsset = null;
        IReadOnlyList<LoanScheduleEntry>? scheduleEntries = null;

        if (request.Type == TradeType.LoanBorrow &&
            request.AmortAnnualRate.HasValue &&
            request.AmortTermMonths.HasValue &&
            request.FirstPaymentDate.HasValue)
        {
            liabilityAsset = new AssetItem(
                Guid.NewGuid(),
                request.LoanLabel,
                FinancialType.Liability,
                null,
                "TWD",
                DateOnly.FromDateTime(DateTime.Today),
                IsActive: true,
                UpdatedAt: null,
                LoanAnnualRate: request.AmortAnnualRate,
                LoanTermMonths: request.AmortTermMonths,
                LoanStartDate: request.FirstPaymentDate,
                LoanHandlingFee: request.Fee > 0 ? request.Fee : null);
            scheduleEntries = AmortizationService.Generate(
                liabilityAsset.Id,
                request.CashAmount,
                request.AmortAnnualRate.Value,
                request.AmortTermMonths.Value,
                request.FirstPaymentDate.Value);
        }

        var mainTrade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: request.LoanLabel,
            Exchange: string.Empty,
            Name: request.LoanLabel,
            Type: request.Type,
            TradeDate: request.TradeDate,
            Price: 0,
            Quantity: 1,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: request.CashAmount,
            CashAccountId: request.CashAccountId,
            Note: request.Note,
            LoanLabel: request.LoanLabel,
            Principal: request.Principal,
            InterestPaid: request.InterestPaid);

        ct.ThrowIfCancellationRequested();
        await _transactionService.RecordAsync(mainTrade).ConfigureAwait(false);

        if (request.Fee > 0)
        {
            ct.ThrowIfCancellationRequested();
            await _transactionService.RecordAsync(new Trade(
                Id: Guid.NewGuid(),
                Symbol: string.Empty,
                Exchange: string.Empty,
                Name: "手續費",
                Type: TradeType.Withdrawal,
                TradeDate: request.TradeDate,
                Price: 0,
                Quantity: 1,
                RealizedPnl: null,
                RealizedPnlPct: null,
                CashAmount: request.Fee,
                CashAccountId: request.CashAccountId,
                Note: string.IsNullOrWhiteSpace(request.Note)
                    ? $"{request.LoanLabel} 手續費"
                    : $"{request.LoanLabel} 手續費 — {request.Note}",
                ParentTradeId: mainTrade.Id)).ConfigureAwait(false);
        }

        if (liabilityAsset is not null)
            await _assetRepository.AddItemAsync(liabilityAsset).ConfigureAwait(false);

        if (scheduleEntries is not null)
            await _loanScheduleRepository.BulkInsertAsync(scheduleEntries).ConfigureAwait(false);

        return new LoanMutationResult(liabilityAsset?.Id, scheduleEntries);
    }
}
