using Assetra.Application.Portfolio.Contracts;
using Assetra.Core.Interfaces;

namespace Assetra.Application.Portfolio.Services;

public sealed class LiabilityMutationWorkflowService : ILiabilityMutationWorkflowService
{
    private readonly IAssetRepository _assetRepository;
    private readonly ITradeRepository _tradeRepository;

    public LiabilityMutationWorkflowService(IAssetRepository assetRepository, ITradeRepository tradeRepository)
    {
        _assetRepository = assetRepository;
        _tradeRepository = tradeRepository;
    }

    public async Task<LiabilityDeletionResult> DeleteAsync(LiabilityDeletionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (!request.AssetId.HasValue && string.IsNullOrEmpty(request.LoanLabel))
            return new LiabilityDeletionResult(false);

        await _tradeRepository.RemoveByLiabilityAsync(request.AssetId, request.LoanLabel, ct).ConfigureAwait(false);

        if (request.AssetId.HasValue)
            await _assetRepository.DeleteItemAsync(request.AssetId.Value).ConfigureAwait(false);

        return new LiabilityDeletionResult(true);
    }
}
