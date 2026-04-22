using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Contracts;

public interface ILoanPaymentWorkflowService
{
    Task<LoanPaymentResult> RecordAsync(
        LoanPaymentRequest request,
        CancellationToken ct = default);
}
