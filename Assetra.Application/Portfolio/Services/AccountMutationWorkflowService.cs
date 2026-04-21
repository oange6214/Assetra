using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;

namespace Assetra.Application.Portfolio.Services;

public sealed class AccountMutationWorkflowService : IAccountMutationWorkflowService
{
    private readonly IAssetRepository _assetRepository;

    public AccountMutationWorkflowService(IAssetRepository assetRepository)
    {
        _assetRepository = assetRepository;
    }

    public Task ArchiveAsync(Guid accountId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return _assetRepository.ArchiveItemAsync(accountId);
    }

    public async Task<AccountDeletionResult> DeleteAsync(Guid accountId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var refs = await _assetRepository
            .HasTradeReferencesAsync(accountId, ct)
            .ConfigureAwait(false);
        if (refs > 0)
            return new AccountDeletionResult(false, refs);

        await _assetRepository.DeleteItemAsync(accountId).ConfigureAwait(false);
        return new AccountDeletionResult(true);
    }
}
