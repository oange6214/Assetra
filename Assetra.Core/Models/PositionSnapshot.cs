namespace Assetra.Core.Models;

/// <summary>
/// Pure projection of a position over the trade journal.
/// Produced by <c>IPositionQueryService</c> (Assetra.Core.Interfaces).
/// </summary>
public sealed record PositionSnapshot(
    Guid PortfolioEntryId,
    decimal Quantity,      // Σ Buy.Qty + Σ StockDiv.Qty − Σ Sell.Qty
    decimal TotalCost,     // Running balance after proportional deduction
    decimal AverageCost,   // TotalCost / Quantity, 0 when Quantity = 0
    decimal RealizedPnl,   // Σ realized P&L from all Sell trades
    DateOnly? FirstBuyDate // Date of first Buy trade, null if no Buy yet
);
