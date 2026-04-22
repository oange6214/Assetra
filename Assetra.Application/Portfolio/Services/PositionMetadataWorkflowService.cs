using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;

namespace Assetra.Application.Portfolio.Services;

public sealed class PositionMetadataWorkflowService : IPositionMetadataWorkflowService
{
    private readonly IPortfolioRepository _portfolioRepository;

    public PositionMetadataWorkflowService(IPortfolioRepository portfolioRepository)
    {
        _portfolioRepository = portfolioRepository;
    }

    public async Task UpdateAsync(
        PositionMetadataUpdateRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        foreach (var entryId in request.EntryIds)
        {
            ct.ThrowIfCancellationRequested();
            await _portfolioRepository
                .UpdateMetadataAsync(entryId, request.Name.Trim(), request.Currency)
                .ConfigureAwait(false);
        }
    }
}
