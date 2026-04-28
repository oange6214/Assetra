using Assetra.Core.Models;

namespace Assetra.Application.Analysis;

/// <summary>
/// 純函式：將 trade journal 偵測為 <see cref="PortfolioEvent"/> 序列，給 TrendsView annotation 使用。
/// 偵測規則：
///   - LargeTrade：單筆 Buy/Sell 金額 ≥ <paramref name="largeTradeThreshold"/>。
///   - FirstDividend：每個 Symbol 的第一筆 CashDividend。
/// 不含 I/O，不含 portfolio 估值依賴 — 純粹從 trade list 推導。
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
}
