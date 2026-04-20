namespace Assetra.Core.Models;

public sealed record PortfolioDailySnapshot(
    DateOnly SnapshotDate,
    decimal TotalCost,
    decimal MarketValue,
    decimal Pnl,
    int PositionCount);
