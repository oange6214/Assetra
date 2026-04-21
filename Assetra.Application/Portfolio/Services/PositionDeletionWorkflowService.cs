using Assetra.AppLayer.Portfolio.Contracts;
using Assetra.AppLayer.Portfolio.Dtos;
using Assetra.Core.Interfaces;

namespace Assetra.AppLayer.Portfolio.Services;

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

        var entryIds = request.EntryIds.ToHashSet();
        var allTrades = await _tradeRepository.GetAllAsync().ConfigureAwait(false);
        foreach (var trade in allTrades.Where(t => t.PortfolioEntryId.HasValue && entryIds.Contains(t.PortfolioEntryId.Value)))
        {
            ct.ThrowIfCancellationRequested();
            await _tradeRepository.RemoveChildrenAsync(trade.Id).ConfigureAwait(false);
            await _tradeRepository.RemoveAsync(trade.Id).ConfigureAwait(false);
        }

        foreach (var entryId in request.EntryIds)
        {
            ct.ThrowIfCancellationRequested();
            await _portfolioRepository.RemoveAsync(entryId).ConfigureAwait(false);
        }
    }
}
