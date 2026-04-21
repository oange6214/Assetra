using Assetra.AppLayer.Portfolio.Dtos;

namespace Assetra.AppLayer.Portfolio.Contracts;

public interface ILoanPaymentWorkflowService
{
    Task<LoanPaymentResult> RecordAsync(
        LoanPaymentRequest request,
        CancellationToken ct = default);
}
