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
        var (sym, exch) = SplitSymbol(symbol);
        var span = period.End.DayNumber - period.Start.DayNumber + 1;
        var chartPeriod = span <= 31 ? ChartPeriod.OneMonth
            : span <= 95 ? ChartPeriod.ThreeMonths
            : span <= 370 ? ChartPeriod.OneYear
            : ChartPeriod.TwoYears;

        var candles = await _history.GetHistoryAsync(sym, exch, chartPeriod, ct).ConfigureAwait(false);
        if (candles.Count < 2) return null;

        var inRange = candles.Where(c => c.Date >= period.Start && c.Date <= period.End).ToList();
        if (inRange.Count < 2) return null;

        var startPx = inRange.OrderBy(c => c.Date).First().Close;
        var endPx = inRange.OrderByDescending(c => c.Date).First().Close;
        if (startPx == 0) return null;

        return (endPx - startPx) / startPx;
    }

    private static (string symbol, string exchange) SplitSymbol(string raw)
    {
        var idx = raw.LastIndexOf('.');
        return idx > 0 && idx < raw.Length - 1
            ? (raw[..idx], raw[(idx + 1)..])
            : (raw, "TW");
    }
}
