using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Services;

public sealed class CreditCardMutationWorkflowService : ICreditCardMutationWorkflowService
{
    private readonly IAssetRepository _assetRepository;

    public CreditCardMutationWorkflowService(IAssetRepository assetRepository)
    {
        _assetRepository = assetRepository;
    }

    public async Task<CreditCardUpsertResult> CreateAsync(CreateCreditCardRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var creditCard = new AssetItem(
            Guid.NewGuid(),
            request.Name.Trim(),
            FinancialType.Liability,
            null,
            request.Currency,
            request.CreatedDate,
            LiabilitySubtype: LiabilitySubtype.CreditCard,
            BillingDay: request.BillingDay,
            DueDay: request.DueDay,
            CreditLimit: request.CreditLimit,
            IssuerName: string.IsNullOrWhiteSpace(request.IssuerName) ? null : request.IssuerName.Trim(),
            Subtype: string.IsNullOrWhiteSpace(request.Subtype) ? null : request.Subtype.Trim());

        await _assetRepository.AddItemAsync(creditCard).ConfigureAwait(false);
        return new CreditCardUpsertResult(creditCard);
    }

    public async Task<CreditCardUpsertResult> UpdateAsync(UpdateCreditCardRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var creditCard = new AssetItem(
            request.CardId,
            request.Name.Trim(),
            FinancialType.Liability,
            null,
            request.Currency,
            request.CreatedDate,
            LiabilitySubtype: LiabilitySubtype.CreditCard,
            BillingDay: request.BillingDay,
            DueDay: request.DueDay,
            CreditLimit: request.CreditLimit,
            IssuerName: string.IsNullOrWhiteSpace(request.IssuerName) ? null : request.IssuerName.Trim(),
            Subtype: string.IsNullOrWhiteSpace(request.Subtype) ? null : request.Subtype.Trim());

        await _assetRepository.UpdateItemAsync(creditCard).ConfigureAwait(false);
        return new CreditCardUpsertResult(creditCard);
    }
}
