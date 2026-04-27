namespace Assetra.Core.Models.Analysis;

public sealed record PerformanceResult(
    PerformancePeriod Period,
    decimal? Xirr,
    decimal? Twr,
    decimal? Mwr,
    decimal? BenchmarkTwr,
    IReadOnlyList<AttributionBucket> Attribution)
{
    public decimal? AlphaOverBenchmark =>
        Twr is not null && BenchmarkTwr is not null ? Twr - BenchmarkTwr : null;
}
