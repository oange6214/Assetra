using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models;
using Assetra.Core.Models.Analysis;

namespace Assetra.Application.Analysis;

/// <summary>
/// Decomposes period investment P&amp;L into four buckets: Realized, Dividend, Commission, Unrealized Δ.
/// Unrealized Δ = end-of-period MarketValue minus start-of-period MarketValue, minus net invested cash flow during period.
/// </summary>
public sealed class PnlAttributionService : IPnlAttributionService
{
    private readonly ITradeRepository _trades;
    private readonly IPortfolioSnapshotRepository _snapshots;

    public PnlAttributionService(ITradeRepository trades, IPortfolioSnapshotRepository snapshots)
    {
        _trades = trades;
        _snapshots = snapshots;
    }

    public async Task<IReadOnlyList<AttributionBucket>> ComputeAsync(PerformancePeriod period, CancellationToken ct = default)
    {
        var trades = await _trades.GetAllAsync().ConfigureAwait(false);
        var inPeriod = trades
            .Where(t =>
            {
                var d = DateOnly.FromDateTime(t.TradeDate);
                return d >= period.Start && d <= period.End;
            })
            .ToList();

        var realized = inPeriod
            .Where(t => t.Type == TradeType.Sell)
            .Sum(t => t.RealizedPnl ?? 0m);

        var dividends = inPeriod
            .Where(t => t.Type == TradeType.CashDividend)
            .Sum(t => t.CashAmount ?? (decimal)t.Quantity * t.Price);

        var commission = -inPeriod.Sum(t => t.Commission ?? 0m);

        var startSnap = await _snapshots.GetSnapshotAsync(period.Start).ConfigureAwait(false);
        var endSnap = await _snapshots.GetSnapshotAsync(period.End).ConfigureAwait(false);
        decimal? unrealizedDelta = null;
        if (startSnap is not null && endSnap is not null)
        {
            var netInvested = inPeriod.Sum(t => t.Type switch
            {
                TradeType.Buy => (decimal)t.Quantity * t.Price,
                TradeType.Sell => -(decimal)t.Quantity * t.Price,
                _ => 0m,
            });
            unrealizedDelta = endSnap.MarketValue - startSnap.MarketValue - netInvested;
        }

        var buckets = new List<AttributionBucket>
        {
            new("Realized", realized),
            new("Dividend", dividends),
            new("Commission", commission),
        };
        if (unrealizedDelta is not null)
            buckets.Add(new AttributionBucket("Unrealized Δ", unrealizedDelta.Value));
        return buckets;
    }
}
