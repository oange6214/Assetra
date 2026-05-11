using Assetra.Core.Models;
using Assetra.Core.Models.Analysis;

namespace Assetra.Application.Analysis;

/// <summary>
/// 把交易記錄轉成 TWR/XIRR 用的 <see cref="CashFlow"/> 序列。
/// 從 ReportsViewModel.BuildPerformanceFlows 抽出共用，現由 ReportsViewModel
/// 與 PortfolioHistoryViewModel 兩處呼叫，避免重複實作。
///
/// 公式：
/// - Buy：負流（買股 = 投資戶 cash 流出）
/// - Sell：正流（賣股 = cash 流入）扣手續費
/// - CashDividend：正流（股利現金 = cash 流入）
/// - 其他類型不視為 performance 計算用的 flow
/// </summary>
public static class PerformanceFlowBuilder
{
    public static List<CashFlow> BuildPerformanceFlows(
        IReadOnlyList<Trade> trades,
        PerformancePeriod period,
        IReadOnlyDictionary<Guid, string>? entryCurrency = null)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(period);
        entryCurrency ??= new Dictionary<Guid, string>();

        var flows = new List<CashFlow>();
        foreach (var trade in trades)
        {
            var date = PerformancePeriod.ToPeriodDate(trade.TradeDate);
            if (!period.Contains(date)) continue;

            var amount = trade.Type switch
            {
                TradeType.Buy => -((decimal)trade.Quantity * trade.Price + (trade.Commission ?? 0m)),
                TradeType.Sell => (decimal)trade.Quantity * trade.Price - (trade.Commission ?? 0m),
                TradeType.CashDividend => trade.CashAmount ?? (decimal)trade.Quantity * trade.Price,
                _ => 0m,
            };
            if (amount == 0m) continue;

            string? currency = null;
            if (trade.PortfolioEntryId is { } entryId
                && entryCurrency.TryGetValue(entryId, out var entryCcy)
                && !string.IsNullOrWhiteSpace(entryCcy))
            {
                currency = entryCcy;
            }

            flows.Add(new CashFlow(date, amount, currency));
        }
        return flows;
    }
}
