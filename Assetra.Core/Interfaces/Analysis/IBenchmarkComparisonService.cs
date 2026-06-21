using Assetra.Core.Models.Analysis;

namespace Assetra.Core.Interfaces.Analysis;

public interface IBenchmarkComparisonService
{
    /// <summary>
    /// Returns the TWR of the benchmark symbol over the given period (e.g. 0050.TW).
    /// Null when price history is unavailable.
    /// </summary>
    Task<decimal?> ComputeBenchmarkTwrAsync(string symbol, PerformancePeriod period, CancellationToken ct = default);

    /// <summary>
    /// Returns the benchmark's normalized return path over the period — each in-range trading day's
    /// cumulative return from the period start (<c>close / startClose − 1</c>). Null when price
    /// history is unavailable. Used to overlay the benchmark as a line on the trends chart.
    /// </summary>
    Task<IReadOnlyList<BenchmarkSeriesPoint>?> ComputeBenchmarkSeriesAsync(
        string symbol, PerformancePeriod period, CancellationToken ct = default);
}
