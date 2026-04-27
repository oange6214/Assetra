using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models;
using Assetra.Core.Models.Analysis;

namespace Assetra.Application.Analysis;

/// <summary>
/// MWR for portfolio: external investment flows (Buy outflow, Sell inflow, CashDividend inflow)
/// + synthetic starting position outflow + synthetic terminal market-value inflow → XIRR.
/// </summary>
public sealed class MoneyWeightedReturnCalculator : IMoneyWeightedReturnCalculator
{
    private readonly ITradeRepository _trades;
    private readonly IPortfolioSnapshotRepository _snapshots;
    private readonly IXirrCalculator _xirr;

    public MoneyWeightedReturnCalculator(
        ITradeRepository trades,
        IPortfolioSnapshotRepository snapshots,
        IXirrCalculator xirr)
    {
        _trades = trades;
        _snapshots = snapshots;
        _xirr = xirr;
    }

    public async Task<decimal?> ComputeAsync(PerformancePeriod period, CancellationToken ct = default)
    {
        var all = await _trades.GetAllAsync().ConfigureAwait(false);
        var flows = BuildFlows(all, period);

        var startSnap = await _snapshots.GetSnapshotAsync(period.Start).ConfigureAwait(false);
        var endSnap = await _snapshots.GetSnapshotAsync(period.End).ConfigureAwait(false);

        if (startSnap is { MarketValue: > 0 })
            flows.Insert(0, new CashFlow(period.Start, -startSnap.MarketValue));
        if (endSnap is { MarketValue: > 0 })
            flows.Add(new CashFlow(period.End, endSnap.MarketValue));

        return _xirr.Compute(flows);
    }

    public async Task<decimal?> ComputeForEntryAsync(Guid portfolioEntryId, PerformancePeriod period, CancellationToken ct = default)
    {
        var all = await _trades.GetAllAsync().ConfigureAwait(false);
        var entryTrades = all.Where(t => t.PortfolioEntryId == portfolioEntryId).ToList();
        var flows = BuildFlows(entryTrades, period);
        if (flows.Count < 2) return null;
        return _xirr.Compute(flows);
    }

    private static List<CashFlow> BuildFlows(IReadOnlyList<Trade> trades, PerformancePeriod period)
    {
        var flows = new List<CashFlow>();
        foreach (var t in trades)
        {
            var d = DateOnly.FromDateTime(t.TradeDate);
            if (d < period.Start || d > period.End) continue;

            var amt = t.Type switch
            {
                TradeType.Buy => -((decimal)t.Quantity * t.Price + (t.Commission ?? 0m)),
                TradeType.Sell => (decimal)t.Quantity * t.Price - (t.Commission ?? 0m),
                TradeType.CashDividend => t.CashAmount ?? (decimal)t.Quantity * t.Price,
                _ => 0m,
            };
            if (amt != 0m) flows.Add(new CashFlow(d, amt));
        }
        return flows;
    }
}
