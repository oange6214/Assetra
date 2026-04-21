using Assetra.AppLayer.Portfolio.Dtos;

namespace Assetra.AppLayer.Portfolio.Contracts;

public interface ILoanMutationWorkflowService
{
    Task<TransactionWorkflowPlan> RecordAsync(
        LoanTransactionRequest request,
        CancellationToken ct = default);
}
