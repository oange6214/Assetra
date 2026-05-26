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

    public async Task UpdateGroupAsync(
        PositionGroupUpdateRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var entries = await _portfolioRepository.GetEntriesAsync(ct).ConfigureAwait(false);
        var byId = entries.ToDictionary(entry => entry.Id);

        foreach (var entryId in request.EntryIds)
        {
            ct.ThrowIfCancellationRequested();
            if (!byId.TryGetValue(entryId, out var entry))
                throw new InvalidOperationException($"Portfolio entry '{entryId}' was not found.");

            await _portfolioRepository
                .UpdateAsync(entry with { PortfolioGroupId = request.PortfolioGroupId }, ct)
                .ConfigureAwait(false);
        }
    }
}
