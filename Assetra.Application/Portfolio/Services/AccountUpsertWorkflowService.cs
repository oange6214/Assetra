using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Services;

public sealed class AccountUpsertWorkflowService : IAccountUpsertWorkflowService
{
    private readonly IAssetRepository _assetRepository;

    public AccountUpsertWorkflowService(IAssetRepository assetRepository)
    {
        _assetRepository = assetRepository;
    }

    public async Task<AccountUpsertResult> CreateAsync(CreateAccountRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var account = new AssetItem(
            Guid.NewGuid(),
            request.Name.Trim(),
            FinancialType.Asset,
            null,
            request.Currency,
            request.CreatedDate);

        await _assetRepository.AddItemAsync(account).ConfigureAwait(false);
        return new AccountUpsertResult(account);
    }

    public async Task<AccountUpsertResult> UpdateAsync(UpdateAccountRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var account = new AssetItem(
            request.AccountId,
            request.Name.Trim(),
            FinancialType.Asset,
            null,
            request.Currency,
            request.CreatedDate);

        await _assetRepository.UpdateItemAsync(account).ConfigureAwait(false);
        return new AccountUpsertResult(account);
    }
}
