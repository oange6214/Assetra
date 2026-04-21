using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Contracts;

public interface ILoanMutationWorkflowService
{
    Task<TransactionWorkflowPlan> RecordAsync(
        LoanTransactionRequest request,
        CancellationToken ct = default);
}
