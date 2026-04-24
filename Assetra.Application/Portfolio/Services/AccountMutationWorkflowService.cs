using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;

namespace Assetra.Application.Portfolio.Services;

public sealed class AccountMutationWorkflowService : IAccountMutationWorkflowService
{
    private readonly IAssetRepository _assetRepository;
    private readonly ITradeRepository _tradeRepository;

    public AccountMutationWorkflowService(IAssetRepository assetRepository, ITradeRepository tradeRepository)
    {
        _assetRepository = assetRepository;
        _tradeRepository = tradeRepository;
    }

    public Task ArchiveAsync(Guid accountId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return _assetRepository.ArchiveItemAsync(accountId);
    }

    public async Task<AccountDeletionResult> DeleteAsync(Guid accountId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await _tradeRepository.RemoveByAccountIdAsync(accountId, ct).ConfigureAwait(false);
        await _assetRepository.DeleteItemAsync(accountId).ConfigureAwait(false);
        return new AccountDeletionResult(true);
    }
}
