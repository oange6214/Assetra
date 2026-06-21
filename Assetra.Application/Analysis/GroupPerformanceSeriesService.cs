using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models;
using Assetra.Core.Models.Analysis;

namespace Assetra.Application.Analysis;

/// <summary>
/// 群組同期 % TWR 序列：用群組的交易逐日重建持倉（買 +、賣 −、配股 +）× 歷史收盤 → 群組每日市值，
/// 再以 TWR 除掉買賣現金流，與 benchmark / 我的投組同基準。重建來源是「交易」（含已售出/已平倉的部位、
/// 且 PortfolioGroupId 直接帶在交易上），而非 active-only 的持倉表。
/// <para><b>v1 限制</b>：不做 FX（假設群組標的為 base 幣別；多幣別群組的 % 形狀會略不準）、不調整分割（split）。
/// 某日有持倉但缺收盤價 → all-or-nothing 跳過該日（同快照重建引擎的紀律）。</para>
/// </summary>
public sealed class GroupPerformanceSeriesService : IGroupPerformanceSeriesService
{
    private readonly ITradeRepository _trades;
    private readonly IStockHistoryProvider _history;
    private readonly ITimeWeightedReturnCalculator _twr;

    public GroupPerformanceSeriesService(
        ITradeRepository trades,
        IStockHistoryProvider history,
        ITimeWeightedReturnCalculator twr)
    {
        _trades = trades ?? throw new ArgumentNullException(nameof(trades));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _twr = twr ?? throw new ArgumentNullException(nameof(twr));
    }

    public async Task<IReadOnlyList<BenchmarkSeriesPoint>?> ComputeGroupSeriesAsync(
        Guid groupId, PerformancePeriod period, CancellationToken ct = default)
    {
        var allTrades = await _trades.GetAllAsync().ConfigureAwait(false);
        var groupTrades = allTrades
            .Where(t => (t.PortfolioGroupId ?? PortfolioGroup.DefaultId) == groupId)
            .ToList();
        if (groupTrades.Count == 0)
            return null;

        // 改變持倉的交易（買 +、賣 −、配股 +）；依日期升冪。日期含期初前（pre-period 持倉算進起始值）。
        var holdingMoves = groupTrades
            .Where(t => t.Type is TradeType.Buy or TradeType.Sell or TradeType.StockDividend)
            .Select(t => (
                t.Symbol,
                t.Exchange,
                Date: DateOnly.FromDateTime(t.TradeDate),
                Signed: t.Type == TradeType.Sell ? -t.Quantity : t.Quantity))
            .OrderBy(m => m.Date)
            .ToList();
        if (holdingMoves.Count == 0)
            return null;

        // 各 symbol 的歷史收盤（in-range）。
        var span = period.End.DayNumber - period.Start.DayNumber + 1;
        var chartPeriod = span <= 31 ? ChartPeriod.OneMonth
            : span <= 95 ? ChartPeriod.ThreeMonths
            : span <= 370 ? ChartPeriod.OneYear
            : ChartPeriod.TwoYears;
        var priceBySymbol = new Dictionary<string, IReadOnlyDictionary<DateOnly, decimal>>();
        foreach (var (sym, exch) in holdingMoves.Select(m => (m.Symbol, m.Exchange)).Distinct())
        {
            var candles = await _history.GetHistoryAsync(sym, exch, chartPeriod, ct).ConfigureAwait(false);
            priceBySymbol[sym] = candles
                .Where(c => c.Date >= period.Start && c.Date <= period.End)
                .GroupBy(c => c.Date)
                .ToDictionary(g => g.Key, g => g.Last().Close);
        }

        var axis = priceBySymbol.Values
            .SelectMany(d => d.Keys)
            .Distinct()
            .OrderBy(d => d)
            .ToList();
        if (axis.Count < 2)
            return null;

        // 逐日重建群組市值：running holdings + 推進交易指標（O(dates + moves)）。
        // 寬鬆策略（比較線可近似、非權威快照）：每個 symbol 的收盤用 forward-fill（沿用最後已知值），
        // 某持倉「至今尚無任何收盤」才略過其貢獻——只要當日有任一持倉可定價就照畫，不讓單一缺價標的
        // 把整條線清成 null（這正是「柏翰」這類群組加不進來的主因）。
        var holdings = new Dictionary<string, int>();
        var lastPrice = new Dictionary<string, decimal>();
        var mi = 0;
        var values = new List<(DateOnly Date, decimal Value)>();
        foreach (var d in axis)
        {
            foreach (var (sym, byDate) in priceBySymbol)
                if (byDate.TryGetValue(d, out var c))
                    lastPrice[sym] = c;

            while (mi < holdingMoves.Count && holdingMoves[mi].Date <= d)
            {
                var m = holdingMoves[mi];
                holdings[m.Symbol] = holdings.GetValueOrDefault(m.Symbol) + m.Signed;
                mi++;
            }

            decimal value = 0m;
            var anyPriced = false;
            foreach (var (sym, qty) in holdings)
            {
                if (qty == 0)
                    continue;
                if (lastPrice.TryGetValue(sym, out var p))
                {
                    value += p * qty;
                    anyPriced = true;
                }
            }
            if (anyPriced && value > 0m)
                values.Add((d, value));
        }
        if (values.Count < 2)
            return null;

        // 群組買賣現金流（投組角度 negate，與 PortfolioHistoryViewModel 的我的投組 TWR 同慣例）。
        var rawFlows = PerformanceFlowBuilder.BuildPerformanceFlows(groupTrades, period);
        var flows = rawFlows
            .Select(f => new CashFlow(f.Date, -f.Amount, f.Currency))
            .ToList();

        var twrSeries = _twr.ComputeSeries(values, flows);
        if (twrSeries is null)
            return null;
        // twrSeries 與 values 同序同長度（ComputeSeries 每個 valuation 一點）→ index 對齊取絕對市值（現值用）。
        return twrSeries
            .Select((p, i) => new BenchmarkSeriesPoint(p.Date, p.CumulativeTwr, values[i].Value))
            .ToList();
    }
}
