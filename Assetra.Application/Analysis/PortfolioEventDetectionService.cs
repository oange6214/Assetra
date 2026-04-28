using Assetra.Core.Models;

namespace Assetra.Application.Analysis;

/// <summary>
/// 純函式：將 trade journal 偵測為 <see cref="PortfolioEvent"/> 序列，給 TrendsView annotation 使用。
/// 偵測規則：
///   - LargeTrade：單筆 Buy/Sell 金額 ≥ <paramref name="largeTradeThreshold"/>。
///   - FirstDividend：每個 Symbol 的第一筆 CashDividend。
///   - YearlyExtreme：每個年度的市值最高點與最低點（v0.17.1，需要 snapshot 序列）。
/// 不含 I/O，不含 portfolio 估值依賴。
/// </summary>
public static class PortfolioEventDetectionService
{
    public static IReadOnlyList<PortfolioEvent> Detect(
        IEnumerable<Trade> trades,
        decimal largeTradeThreshold = 100_000m)
    {
        ArgumentNullException.ThrowIfNull(trades);
        if (largeTradeThreshold <= 0m)
            throw new ArgumentOutOfRangeException(nameof(largeTradeThreshold));

        var events = new List<PortfolioEvent>();
        var seenDividend = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in trades.OrderBy(x => x.TradeDate))
        {
            if (t.Type is TradeType.Buy or TradeType.Sell)
            {
                var grossAmount = t.Price * t.Quantity;
                if (grossAmount >= largeTradeThreshold)
                {
                    events.Add(new PortfolioEvent(
                        Id: Guid.NewGuid(),
                        Date: DateOnly.FromDateTime(t.TradeDate),
                        Kind: PortfolioEventKind.LargeTrade,
                        Label: $"{t.Type} {t.Symbol} × {t.Quantity}",
                        Description: $"成交金額 {grossAmount:N0}",
                        Amount: grossAmount,
                        Symbol: t.Symbol));
                }
            }
            else if (t.Type == TradeType.CashDividend)
            {
                if (seenDividend.Add(t.Symbol))
                {
                    var amount = t.CashAmount ?? (t.Price * t.Quantity);
                    events.Add(new PortfolioEvent(
                        Id: Guid.NewGuid(),
                        Date: DateOnly.FromDateTime(t.TradeDate),
                        Kind: PortfolioEventKind.FirstDividend,
                        Label: $"{t.Symbol} 首次配息",
                        Description: $"配息金額 {amount:N0}",
                        Amount: amount,
                        Symbol: t.Symbol));
                }
            }
        }

        return events;
    }

    /// <summary>
    /// 從每日快照序列偵測各年度的最高與最低市值點（v0.17.1）。
    /// 規則：
    ///   - 以 <see cref="PortfolioDailySnapshot.SnapshotDate"/> 的年份分組。
    ///   - 每個年份，挑出 <see cref="PortfolioDailySnapshot.MarketValue"/> 最大與最小的那一日。
    ///   - 若該年份只有一筆快照，仍會產出最高與最低（同一筆，兩個事件）。
    ///   - 若最高與最低同日（例如年內僅一筆），會產出兩個事件 — 由 caller 決定是否去重。
    /// </summary>
    public static IReadOnlyList<PortfolioEvent> DetectYearlyExtremes(
        IReadOnlyList<PortfolioDailySnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        if (snapshots.Count == 0) return Array.Empty<PortfolioEvent>();

        var events = new List<PortfolioEvent>();
        var byYear = snapshots.GroupBy(s => s.SnapshotDate.Year).OrderBy(g => g.Key);

        foreach (var group in byYear)
        {
            var ordered = group.OrderBy(s => s.SnapshotDate).ToList();
            var max = ordered.Aggregate((best, cur) => cur.MarketValue > best.MarketValue ? cur : best);
            var min = ordered.Aggregate((best, cur) => cur.MarketValue < best.MarketValue ? cur : best);

            events.Add(new PortfolioEvent(
                Id: Guid.NewGuid(),
                Date: max.SnapshotDate,
                Kind: PortfolioEventKind.YearlyExtreme,
                Label: $"{group.Key} 年度新高",
                Description: $"市值 {max.MarketValue:N0}",
                Amount: max.MarketValue,
                Symbol: null));

            events.Add(new PortfolioEvent(
                Id: Guid.NewGuid(),
                Date: min.SnapshotDate,
                Kind: PortfolioEventKind.YearlyExtreme,
                Label: $"{group.Key} 年度新低",
                Description: $"市值 {min.MarketValue:N0}",
                Amount: min.MarketValue,
                Symbol: null));
        }

        return events;
    }
}
