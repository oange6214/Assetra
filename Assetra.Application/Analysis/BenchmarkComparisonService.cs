using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models;
using Assetra.Core.Models.Analysis;

namespace Assetra.Application.Analysis;

/// <summary>
/// Benchmark TWR = (priceEnd / priceStart) − 1, using close prices from IStockHistoryProvider.
/// </summary>
public sealed class BenchmarkComparisonService : IBenchmarkComparisonService
{
    private readonly IStockHistoryProvider _history;

    public BenchmarkComparisonService(IStockHistoryProvider history)
    {
        _history = history;
    }

    public async Task<decimal?> ComputeBenchmarkTwrAsync(string symbol, PerformancePeriod period, CancellationToken ct = default)
    {
        var inRange = await GetInRangeCandlesAsync(symbol, period, ct).ConfigureAwait(false);
        if (inRange is null)
            return null;

        var startPx = inRange[0].Close;
        var endPx = inRange[^1].Close;
        return startPx == 0 ? null : (endPx - startPx) / startPx;
    }

    public async Task<IReadOnlyList<BenchmarkSeriesPoint>?> ComputeBenchmarkSeriesAsync(
        string symbol, PerformancePeriod period, CancellationToken ct = default)
    {
        var inRange = await GetInRangeCandlesAsync(symbol, period, ct).ConfigureAwait(false);
        if (inRange is null)
            return null;

        var startPx = inRange[0].Close;
        if (startPx == 0)
            return null;

        // 每個交易日相對區間起點的累積報酬 = close/startPx − 1（起點本身 = 0%）；Value = 當日收盤（現價用）。
        return inRange
            .Select(c => new BenchmarkSeriesPoint(c.Date, (c.Close - startPx) / startPx, c.Close))
            .ToList();
    }

    /// <summary>
    /// Fetches the benchmark's candles and returns those within <paramref name="period"/> in date
    /// order. Null when there are fewer than 2 usable points (can't form a return). Shared by the
    /// TWR endpoint and the full normalized series.
    /// </summary>
    private async Task<IReadOnlyList<OhlcvPoint>?> GetInRangeCandlesAsync(
        string symbol, PerformancePeriod period, CancellationToken ct)
    {
        var (sym, exch) = SplitSymbol(symbol);
        var span = period.End.DayNumber - period.Start.DayNumber + 1;
        var chartPeriod = span <= 31 ? ChartPeriod.OneMonth
            : span <= 95 ? ChartPeriod.ThreeMonths
            : span <= 370 ? ChartPeriod.OneYear
            : ChartPeriod.TwoYears;

        var candles = await _history.GetHistoryAsync(sym, exch, chartPeriod, ct).ConfigureAwait(false);
        if (candles.Count < 2)
            return null;

        var inRange = candles
            .Where(c => c.Date >= period.Start && c.Date <= period.End)
            .OrderBy(c => c.Date)
            .ToList();
        return inRange.Count < 2 ? null : inRange;
    }

    private static (string symbol, string exchange) SplitSymbol(string raw)
    {
        var idx = raw.LastIndexOf('.');
        return idx > 0 && idx < raw.Length - 1
            ? (raw[..idx], raw[(idx + 1)..])
            : (raw, "TW");
    }
}
