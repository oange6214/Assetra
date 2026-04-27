namespace Assetra.Core.Models.Analysis;

public sealed record RiskMetrics(
    decimal? AnnualizedVolatility,
    decimal? MaxDrawdown,
    decimal? SharpeRatio,
    decimal? Hhi,
    IReadOnlyList<ConcentrationBucket> TopHoldings)
{
    /// <summary>
    /// True when any single holding exceeds 30% weight or HHI exceeds 0.30.
    /// Flag is consumed by the UI to surface a concentration warning without
    /// requiring a refactor of the price-target-specific Alerts framework.
    /// </summary>
    public bool HasConcentrationWarning =>
        (Hhi ?? 0m) > 0.30m || TopHoldings.Any(b => b.Weight > 0.30m);
}
