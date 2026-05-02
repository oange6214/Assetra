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
                LoanHandlingFee: request.Fee > 0 ? request.Fee : null,
                LiabilitySubtype: LiabilitySubtype.Loan,
                Subtype: string.IsNullOrWhiteSpace(request.Subtype) ? null : request.Subtype.Trim());
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

        Trade? feeTrade = null;
        if (request.Fee > 0)
        {
            feeTrade = new Trade(
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
                ParentTradeId: mainTrade.Id);
        }

        var writtenTrades = new List<Trade>();
        var assetAdded = false;
        var scheduleTouched = false;

        try
        {
            ct.ThrowIfCancellationRequested();
            await _transactionService.RecordAsync(mainTrade).ConfigureAwait(false);
            writtenTrades.Add(mainTrade);

            if (feeTrade is not null)
            {
                ct.ThrowIfCancellationRequested();
                await _transactionService.RecordAsync(feeTrade).ConfigureAwait(false);
                writtenTrades.Add(feeTrade);
            }

            if (liabilityAsset is not null)
            {
                await _assetRepository.AddItemAsync(liabilityAsset).ConfigureAwait(false);
                assetAdded = true;
            }

            if (scheduleEntries is not null)
            {
                scheduleTouched = true;
                await _loanScheduleRepository.BulkInsertAsync(scheduleEntries).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            try
            {
                await RollBackLoanArtifactsAsync(liabilityAsset, assetAdded, scheduleTouched, writtenTrades).ConfigureAwait(false);
            }
            catch (Exception cleanupEx)
            {
                throw new InvalidOperationException(
                    "貸款交易建立失敗，且回復未完成資料時也失敗。",
                    new AggregateException(ex, cleanupEx));
            }

            throw new InvalidOperationException("貸款交易建立失敗，已嘗試回復未完成資料。", ex);
        }

        return new LoanMutationResult(liabilityAsset?.Id, scheduleEntries);
    }

    private async Task RollBackLoanArtifactsAsync(
        AssetItem? liabilityAsset,
        bool assetAdded,
        bool scheduleTouched,
        IReadOnlyList<Trade> writtenTrades)
    {
        List<Exception>? cleanupErrors = null;

        if (liabilityAsset is not null && scheduleTouched)
        {
            AddCleanupError(await TryCleanupAsync(
                () => _loanScheduleRepository.DeleteByAssetAsync(liabilityAsset.Id)).ConfigureAwait(false));
        }

        if (liabilityAsset is not null && assetAdded)
        {
            AddCleanupError(await TryCleanupAsync(
                () => _assetRepository.DeleteItemAsync(liabilityAsset.Id)).ConfigureAwait(false));
        }

        foreach (var trade in writtenTrades.Reverse())
        {
            AddCleanupError(await TryCleanupAsync(
                () => _transactionService.DeleteAsync(trade)).ConfigureAwait(false));
        }

        if (cleanupErrors is not null)
            throw new AggregateException("貸款建立失敗後的資料回復也發生錯誤。", cleanupErrors);

        void AddCleanupError(Exception? ex)
        {
            if (ex is null)
                return;
            cleanupErrors ??= [];
            cleanupErrors.Add(ex);
        }
    }

    private static async Task<Exception?> TryCleanupAsync(Func<Task> cleanup)
    {
        try
        {
            await cleanup().ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
