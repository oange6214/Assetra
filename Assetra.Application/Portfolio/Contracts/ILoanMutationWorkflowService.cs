using Assetra.Application.Portfolio.Dtos;
using Assetra.Application.Portfolio.Services;

namespace Assetra.Application.Portfolio.Contracts;

public interface ILoanMutationWorkflowService
{
    Task<LoanMutationResult> RecordAsync(LoanTransactionRequest request, CancellationToken ct = default);
}
