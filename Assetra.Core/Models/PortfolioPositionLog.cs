namespace Assetra.Core.Models;

/// <summary>
/// Immutable record of a portfolio position's state at a specific date.
/// A <see cref="Quantity"/> of zero means the position was closed on that date.
/// </summary>
public sealed record PortfolioPositionLog(
    Guid LogId,
    DateOnly LogDate,
    Guid PositionId,
    string Symbol,
    string Exchange,
    int Quantity,
    decimal BuyPrice);
