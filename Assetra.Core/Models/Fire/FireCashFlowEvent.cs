namespace Assetra.Core.Models.Fire;

public enum FireCashFlowDirection
{
    Inflow = 0,
    Outflow = 1,
}

public enum FireCashFlowGrowthMode
{
    Fixed = 0,
    InflationAdjusted = 1,
    CustomGrowthRate = 2,
}

public sealed record FireCashFlowEvent(
    Guid Id,
    Guid ScenarioId,
    string Name,
    int StartYearOffset,
    int? EndYearOffset,
    decimal AnnualAmount,
    FireCashFlowDirection Direction,
    FireCashFlowGrowthMode GrowthMode,
    decimal? CustomGrowthRate,
    string? Notes);
