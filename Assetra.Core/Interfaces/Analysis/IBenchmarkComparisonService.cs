using Assetra.Core.Models.Analysis;

namespace Assetra.Core.Interfaces.Analysis;

public interface IBenchmarkComparisonService
{
    /// <summary>
    /// Returns the TWR of the benchmark symbol over the given period (e.g. 0050.TW).
    /// Null when price history is unavailable.
    /// </summary>
    Task<decimal?> ComputeBenchmarkTwrAsync(string symbol, PerformancePeriod period, CancellationToken ct = default);
}
