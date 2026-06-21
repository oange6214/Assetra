using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models;
using Assetra.Core.Models.Analysis;

namespace Assetra.Application.Analysis;

/// <summary>
/// Benchmark 同期 % = close / <b>期初基準價</b> − 1。期初基準價 = 「<b>使用者選的期間起點</b>當天或之前
/// 最後一根收盤」（而非該標的第一根 in-range candle）。所以選 5D 就是用「5 天前」的價當起點，所有線
/// 都從同一個期初日 0% 起跑、可公平比較。標的在期初當天無資料（如 00830 五月有缺口）時，仍以期初前
/// 最近一根當基準，並補一個期初 0% 點讓線從期初起跑（缺口段為連線、非真實逐日資料）。
/// </summary>
public sealed class BenchmarkComparisonService : IBenchmarkComparisonService
{
    private readonly IStockHistoryProvider _history;
    private readonly IIntradayHistoryProvider? _intraday;

    public BenchmarkComparisonService(IStockHistoryProvider history, IIntradayHistoryProvider? intraday = null)
    {
        _history = history;
        _intraday = intraday;
    }

    public async Task<decimal?> ComputeBenchmarkTwrAsync(string symbol, PerformancePeriod period, CancellationToken ct = default)
    {
        var r = await GetBaselineAndRangeAsync(symbol, period, ct).ConfigureAwait(false);
        if (r is null)
            return null;
        var (baseline, inRange) = r.Value;
        return (inRange[^1].Close - baseline) / baseline;
    }

    public async Task<IReadOnlyList<BenchmarkSeriesPoint>?> ComputeBenchmarkSeriesAsync(
        string symbol, PerformancePeriod period, IntradayRange? intraday = null, CancellationToken ct = default)
    {
        // 1D/5D：抓盤中分時（帶時分）。抓不到 ≥2 點再退日線、不讓線消失。
        if (intraday is { } range && _intraday is not null)
        {
            var live = await ComputeIntradaySeriesAsync(symbol, range, ct).ConfigureAwait(false);
            if (live is { Count: >= 2 })
                return live;
        }

        var r = await GetBaselineAndRangeAsync(symbol, period, ct).ConfigureAwait(false);
        if (r is null)
            return null;
        var (baseline, inRange) = r.Value;

        var points = new List<BenchmarkSeriesPoint>(inRange.Count + 1);
        // 標的在期初當天無資料時，補一個期初 0% 點（abs = 基準價），讓所有線都從期初起跑、同基準。
        if (inRange[0].Date > period.Start)
            points.Add(new BenchmarkSeriesPoint(period.Start.ToDateTime(TimeOnly.MinValue), 0m, baseline));
        foreach (var c in inRange)
            points.Add(new BenchmarkSeriesPoint(c.Date.ToDateTime(TimeOnly.MinValue), (c.Close - baseline) / baseline, c.Close));
        return points;
    }

    /// <summary>盤中分時 → 同期 %：以盤中第一點收盤為基準，每點 close/baseline − 1。少於 2 點或基準 0 回 null。</summary>
    private async Task<IReadOnlyList<BenchmarkSeriesPoint>?> ComputeIntradaySeriesAsync(
        string symbol, IntradayRange range, CancellationToken ct)
    {
        var (sym, exch) = SplitSymbol(symbol);
        var points = await _intraday!.GetIntradayAsync(sym, exch, range, ct).ConfigureAwait(false);
        if (points.Count < 2)
            return null;
        var baseline = points[0].Close;
        if (baseline == 0m)
            return null;
        return points
            .Select(p => new BenchmarkSeriesPoint(p.At, (p.Close - baseline) / baseline, p.Close))
            .ToList();
    }

    /// <summary>
    /// 抓 candles（窗比期間略長，確保有「期初前」一根可當基準）、回 (期初基準價, in-range candles)。
    /// 基準價 = 期初當天或之前最後一根收盤；無則退回第一根 in-range。少於 2 根 in-range 或基準 0 回 null。
    /// </summary>
    private async Task<(decimal Baseline, IReadOnlyList<OhlcvPoint> InRange)?> GetBaselineAndRangeAsync(
        string symbol, PerformancePeriod period, CancellationToken ct)
    {
        var (sym, exch) = SplitSymbol(symbol);
        var span = period.End.DayNumber - period.Start.DayNumber + 1;
        // 抓比期間略長的窗：閾值比期間長度小一階，確保 fetch 涵蓋「期初前」幾根（找基準用）。
        var chartPeriod = span <= 24 ? ChartPeriod.OneMonth
            : span <= 88 ? ChartPeriod.ThreeMonths
            : span <= 360 ? ChartPeriod.OneYear
            : ChartPeriod.TwoYears;

        var candles = (await _history.GetHistoryAsync(sym, exch, chartPeriod, ct).ConfigureAwait(false))
            .OrderBy(c => c.Date)
            .ToList();
        if (candles.Count < 2)
            return null;

        // 近期短窗（1D/5D，≤ 一週）：日資料沒有盤中 → 取「最近 span 個交易日收盤」(TakeLast)，而非日曆對齊。
        // 這樣 5D = 最近 5 個交易日的折線（不會因快照比行情新撞空窗而退化成 2 點斜線/空白）；基準 = 這段第一根。
        // 只在「延伸到最新」時套用（period.End ≥ 最後一根）；過去的自訂短區間仍走日曆視窗、不假造。
        if (span <= 7 && period.End >= candles[^1].Date)
        {
            var recent = candles.TakeLast(span).ToList();
            return recent.Count < 2 || recent[0].Close == 0m ? null : (recent[0].Close, recent);
        }

        var inRange = candles.Where(c => c.Date >= period.Start && c.Date <= period.End).ToList();

        // 視窗內 K 線不足（短期間 5D/1D 撞週末/假日，或快照比行情新、近幾日尚未發布）→ 若此區間是「延伸
        // 到最新」的近期區間（period.End 不早於最後一根），退用抓回來的最後 2 根，至少畫得出最近走勢、不空白；
        // 基準改用退用段的第一根（不 prepend）。過去的自訂區間（period.End 早於最後一根）維持嚴格、不假造。
        if (inRange.Count < 2)
        {
            if (period.End < candles[^1].Date)
                return null;
            var last2 = candles.TakeLast(2).ToList();
            return last2[0].Close == 0m ? null : (last2[0].Close, last2);
        }

        // 期初基準價：期初當天或之前最後一根收盤（candles 已升冪 → Last()）；無則退回第一根 in-range。
        var baseline = candles
            .Where(c => c.Date <= period.Start)
            .Select(c => c.Close)
            .DefaultIfEmpty(inRange[0].Close)
            .Last();
        return baseline == 0m ? null : (baseline, inRange);
    }

    private static (string symbol, string exchange) SplitSymbol(string raw)
    {
        var idx = raw.LastIndexOf('.');
        return idx > 0 && idx < raw.Length - 1
            ? (raw[..idx], raw[(idx + 1)..])
            : (raw, "TW");
    }
}
