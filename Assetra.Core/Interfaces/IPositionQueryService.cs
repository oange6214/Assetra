using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

/// <summary>
/// Projection service for investment positions.
/// Metadata (<see cref="PortfolioEntry"/>) provides identity;
/// this service computes financial state (quantity, cost, realized P&amp;L) from
/// <see cref="ITradeRepository"/>.
/// </summary>
public interface IPositionQueryService
{
    /// <summary>
    /// Running-balance projection for one position.
    /// Returns null when no trades reference the entry.
    /// </summary>
    Task<PositionSnapshot?> GetPositionAsync(Guid portfolioEntryId);

    /// <summary>
    /// Single pass over all trades grouped by PortfolioEntryId.
    /// UI list load should use this to avoid O(n×m).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, PositionSnapshot>> GetAllPositionSnapshotsAsync();

    /// <summary>
    /// Computes realized P&amp;L at the moment of a Sell trade (without persisting).
    /// Called by the TX ViewModel before <c>TradeRepository.AddAsync</c>, so the
    /// resulting value can be stored on the Trade record.
    /// </summary>
    Task<decimal> ComputeRealizedPnlAsync(
        Guid portfolioEntryId,
        DateTime sellDate,
        decimal sellPrice,
        decimal sellQty,
        decimal sellFees);
}
