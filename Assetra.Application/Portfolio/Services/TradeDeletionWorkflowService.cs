using Assetra.AppLayer.Portfolio.Contracts;
using Assetra.AppLayer.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.AppLayer.Portfolio.Services;

public sealed class TradeDeletionWorkflowService : ITradeDeletionWorkflowService
{
    private readonly ITradeRepository _tradeRepository;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly IPositionQueryService _positionQueryService;

    public TradeDeletionWorkflowService(
        ITradeRepository tradeRepository,
        IPortfolioRepository portfolioRepository,
        IPositionQueryService positionQueryService)
    {
        _tradeRepository = tradeRepository;
        _portfolioRepository = portfolioRepository;
        _positionQueryService = positionQueryService;
    }

    public async Task<TradeDeletionResult> DeleteAsync(
        TradeDeletionRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (await WouldRemovalCauseNegativeQtyAsync(request).ConfigureAwait(false))
            return new TradeDeletionResult(Success: false, BlockedBySell: true);

        await ApplyTradeRemovalOnPositionAsync(request, ct).ConfigureAwait(false);
        await _tradeRepository.RemoveChildrenAsync(request.TradeId).ConfigureAwait(false);
        await _tradeRepository.RemoveAsync(request.TradeId).ConfigureAwait(false);

        return new TradeDeletionResult(Success: true);
    }

    private async Task<bool> WouldRemovalCauseNegativeQtyAsync(TradeDeletionRequest request)
    {
        if (request.TradeType is not (TradeType.Buy or TradeType.StockDividend))
            return false;
        if (request.PortfolioEntryId is not { } entryId)
            return false;

        var snapshot = await _positionQueryService.GetPositionAsync(entryId).ConfigureAwait(false);
        return snapshot is not null && snapshot.Quantity - request.Quantity < 0;
    }

    private async Task ApplyTradeRemovalOnPositionAsync(
        TradeDeletionRequest request,
        CancellationToken ct)
    {
        if (request.PortfolioEntryId is not { } entryId)
            return;

        switch (request.TradeType)
        {
            case TradeType.Buy:
            {
                var refs = await _portfolioRepository
                    .HasTradeReferencesAsync(entryId, ct)
                    .ConfigureAwait(false);
                if (refs <= 1)
                    await _portfolioRepository.RemoveAsync(entryId).ConfigureAwait(false);
                break;
            }
            case TradeType.StockDividend:
                break;
            case TradeType.Sell:
            {
                var allEntries = await _portfolioRepository.GetEntriesAsync().ConfigureAwait(false);
                foreach (var entry in allEntries)
                {
                    if (string.Equals(entry.Symbol, request.Symbol, StringComparison.OrdinalIgnoreCase)
                        && !entry.IsActive)
                    {
                        await _portfolioRepository.UnarchiveAsync(entry.Id).ConfigureAwait(false);
                    }
                }
                break;
            }
        }
    }
}
