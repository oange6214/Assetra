using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure;

public sealed class PositionQueryService : IPositionQueryService
{
    private readonly ITradeRepository _tradeRepo;

    public PositionQueryService(ITradeRepository tradeRepo)
    {
        ArgumentNullException.ThrowIfNull(tradeRepo);
        _tradeRepo = tradeRepo;
    }

    public async Task<PositionSnapshot?> GetPositionAsync(Guid portfolioEntryId)
    {
        var all = await _tradeRepo.GetAllAsync().ConfigureAwait(false);
        var trades = all
            .Where(t => t.PortfolioEntryId == portfolioEntryId)
            .OrderBy(t => t.TradeDate)
            .ToList();
        if (trades.Count == 0) return null;
        return Project(portfolioEntryId, trades);
    }

    private static PositionSnapshot Project(Guid entryId, IReadOnlyList<Trade> trades)
    {
        decimal totalCost = 0m;
        decimal totalQty = 0m;
        decimal realizedPnl = 0m;
        DateOnly? firstBuy = null;

        foreach (var t in trades)
        {
            switch (t.Type)
            {
                case TradeType.Buy:
                    totalCost += (t.Price * t.Quantity) + (t.Commission ?? 0m);
                    totalQty += t.Quantity;
                    firstBuy ??= DateOnly.FromDateTime(t.TradeDate);
                    break;

                case TradeType.Sell:
                    if (totalQty > 0m)
                    {
                        var cogs = totalCost * (t.Quantity / totalQty);
                        var proceeds = (t.Price * t.Quantity) - (t.Commission ?? 0m);
                        realizedPnl += proceeds - cogs;
                        totalCost -= cogs;
                    }
                    totalQty -= t.Quantity;
                    break;

                case TradeType.StockDividend:
                    totalQty += t.Quantity;
                    break;
            }
        }

        var avg = totalQty > 0m ? totalCost / totalQty : 0m;
        return new PositionSnapshot(entryId, totalQty, totalCost, avg, realizedPnl, firstBuy);
    }

    public async Task<IReadOnlyDictionary<Guid, PositionSnapshot>> GetAllPositionSnapshotsAsync()
    {
        var all = await _tradeRepo.GetAllAsync().ConfigureAwait(false);
        var result = new Dictionary<Guid, PositionSnapshot>();

        var grouped = all
            .Where(t => t.PortfolioEntryId is not null)
            .GroupBy(t => t.PortfolioEntryId!.Value);

        foreach (var g in grouped)
        {
            var ordered = g.OrderBy(t => t.TradeDate).ToList();
            result[g.Key] = Project(g.Key, ordered);
        }
        return result;
    }

    public async Task<decimal> ComputeRealizedPnlAsync(
        Guid portfolioEntryId,
        DateTime sellDate,
        decimal sellPrice,
        decimal sellQty,
        decimal sellFees)
    {
        var snap = await GetPositionAsync(portfolioEntryId).ConfigureAwait(false);
        if (snap is null || snap.Quantity <= 0m) return 0m;

        var cogs = snap.TotalCost * (sellQty / snap.Quantity);
        var proceeds = (sellPrice * sellQty) - sellFees;
        return proceeds - cogs;
    }
}
