using Assetra.Core.Models;
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
    /// Returns the benchmark's normalized return path over the period — each point's cumulative return
    /// from the start (<c>close / startClose − 1</c>). Null when price history is unavailable.
    /// When <paramref name="intraday"/> is set (1D/5D), fetches intraday minute candles instead of
    /// daily closes, so the line carries actual times; otherwise daily closes (at midnight).
    /// </summary>
    Task<IReadOnlyList<BenchmarkSeriesPoint>?> ComputeBenchmarkSeriesAsync(
        string symbol, PerformancePeriod period, IntradayRange? intraday = null, CancellationToken ct = default);
}
