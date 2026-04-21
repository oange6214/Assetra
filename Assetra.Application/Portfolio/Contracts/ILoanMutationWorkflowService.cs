using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Contracts;

public interface ILoanMutationWorkflowService
{
    Task<LoanMutationResult> RecordAsync(LoanTransactionRequest request, CancellationToken ct = default);
}
