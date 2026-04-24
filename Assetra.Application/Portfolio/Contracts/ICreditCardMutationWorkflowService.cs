using Assetra.Application.Portfolio.Dtos;

namespace Assetra.Application.Portfolio.Contracts;

public interface ICreditCardMutationWorkflowService
{
    Task<CreditCardUpsertResult> CreateAsync(CreateCreditCardRequest request, CancellationToken ct = default);

    Task<CreditCardUpsertResult> UpdateAsync(UpdateCreditCardRequest request, CancellationToken ct = default);
}
