using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Contracts;

public interface ICreditCardTransactionWorkflowService
{
    Task<CreditCardTransactionResult> ChargeAsync(
        CreditCardChargeRequest request,
        CancellationToken ct = default);

    Task<CreditCardTransactionResult> PayAsync(
        CreditCardPaymentRequest request,
        CancellationToken ct = default);
}
