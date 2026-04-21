using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;

namespace Assetra.Application.Portfolio.Services;

public sealed class LoanMutationWorkflowService : ILoanMutationWorkflowService
{
    private readonly ITransactionWorkflowService _transactionWorkflowService;
    private readonly IAssetRepository _assetRepository;
    private readonly ILoanScheduleRepository _loanScheduleRepository;
    private readonly ITransactionService _transactionService;

    public LoanMutationWorkflowService(
        ITransactionWorkflowService transactionWorkflowService,
        IAssetRepository assetRepository,
        ILoanScheduleRepository loanScheduleRepository,
        ITransactionService transactionService)
    {
        _transactionWorkflowService = transactionWorkflowService;
        _assetRepository = assetRepository;
        _loanScheduleRepository = loanScheduleRepository;
        _transactionService = transactionService;
    }

    public async Task<TransactionWorkflowPlan> RecordAsync(
        LoanTransactionRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var plan = _transactionWorkflowService.CreateLoanPlan(request);

        if (plan.LiabilityAsset is not null)
            await _assetRepository.AddItemAsync(plan.LiabilityAsset).ConfigureAwait(false);

        if (plan.LoanScheduleEntries is not null)
            await _loanScheduleRepository.BulkInsertAsync(plan.LoanScheduleEntries).ConfigureAwait(false);

        foreach (var trade in plan.Trades)
        {
            ct.ThrowIfCancellationRequested();
            await _transactionService.RecordAsync(trade).ConfigureAwait(false);
        }

        return plan;
    }
}
