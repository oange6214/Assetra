namespace Assetra.Core.Models.Fire;

public sealed record FireWealthPoint(
    int Year,
    decimal NetWorth);

public sealed record FirePlanningProjection(
    decimal RequiredAssets,
    int? YearsToFire,
    int? FireYear,
    decimal ProjectedNetWorthAtFire,
    decimal RequiredMonthlySavings,
    decimal? MonteCarloSuccessRate,
    IReadOnlyList<FireWealthPoint> AccumulationPath,
    IReadOnlyList<FireDrawdownPoint> DrawdownPath,
    IReadOnlyList<FireProjectionWarning> Warnings);
