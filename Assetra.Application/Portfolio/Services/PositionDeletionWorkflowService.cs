using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;

namespace Assetra.Application.Portfolio.Services;

public sealed class PositionDeletionWorkflowService : IPositionDeletionWorkflowService
{
    private readonly ITradeRepository _tradeRepository;
    private readonly IPortfolioRepository _portfolioRepository;

    public PositionDeletionWorkflowService(
        ITradeRepository tradeRepository,
        IPortfolioRepository portfolioRepository)
    {
        _tradeRepository = tradeRepository;
        _portfolioRepository = portfolioRepository;
    }

    public async Task DeleteAsync(
        PositionDeletionRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (request.EntryIds.Count == 0)
            return;

        var matched = await _tradeRepository
            .GetByPortfolioEntryIdsAsync(request.EntryIds, ct)
            .ConfigureAwait(false);
        foreach (var trade in matched)
        {
            ct.ThrowIfCancellationRequested();
            await _tradeRepository.RemoveChildrenAsync(trade.Id, ct).ConfigureAwait(false);
            await _tradeRepository.RemoveAsync(trade.Id, ct).ConfigureAwait(false);
        }

        foreach (var entryId in request.EntryIds)
        {
            ct.ThrowIfCancellationRequested();
            await _portfolioRepository.RemoveAsync(entryId).ConfigureAwait(false);
        }
    }
}
