using Assetra.Core.Models;
using Assetra.Core.Models.Analysis;

namespace Assetra.Application.Analysis;

/// <summary>
/// 把交易記錄轉成 TWR/XIRR 用的 <see cref="CashFlow"/> 序列。
/// 從 ReportsViewModel.BuildPerformanceFlows 抽出共用，現由 ReportsViewModel
/// 與 PortfolioHistoryViewModel 兩處呼叫，避免重複實作。
///
/// <para>scope = <see cref="PerformanceFlowScope.InvestmentOnly"/>（預設）：</para>
/// - Buy：負流（投資者付出的成本，包含手續費）
/// - Sell：正流（投資者實得，扣手續費）
/// - CashDividend：正流（股利現金）
/// - Income / Deposit / Withdrawal / Transfer / Loan*/CreditCard*：忽略
///   （這些不影響投資 MarketValue；投資 TWR 不該考慮這些 flow）
///
/// <para>scope = <see cref="PerformanceFlowScope.NetWorth"/>：</para>
/// - 上述 InvestmentOnly 的全部，加：
/// - Deposit / Income：正流（資金進入系統）
/// - Withdrawal：負流（資金離開系統）
/// - Transfer：忽略（user 自己的帳戶間移動，系統內 zero-sum）
/// - Loan / CreditCard*：忽略（負債變動不算 cash flow）
/// 此模式用於「全資產淨值」TWR — 配合 daily net-worth snapshot 使用。
/// </summary>
public static class PerformanceFlowBuilder
{
    public static List<CashFlow> BuildPerformanceFlows(
        IReadOnlyList<Trade> trades,
        PerformancePeriod period,
        IReadOnlyDictionary<Guid, string>? entryCurrency = null,
        PerformanceFlowScope scope = PerformanceFlowScope.InvestmentOnly)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(period);
        entryCurrency ??= new Dictionary<Guid, string>();

        var flows = new List<CashFlow>();
        foreach (var trade in trades)
        {
            var date = PerformancePeriod.ToPeriodDate(trade.TradeDate);
            if (!period.Contains(date)) continue;

            var amount = ResolveAmount(trade, scope);
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

    private static decimal ResolveAmount(Trade trade, PerformanceFlowScope scope)
    {
        return trade.Type switch
        {
            // 投資相關（兩種 scope 都計）
            TradeType.Buy => -((decimal)trade.Quantity * trade.Price + (trade.Commission ?? 0m)),
            TradeType.Sell => (decimal)trade.Quantity * trade.Price - (trade.Commission ?? 0m),
            TradeType.CashDividend => trade.CashAmount ?? (decimal)trade.Quantity * trade.Price,
            // 非投資 cash flow（僅 NetWorth scope 計）
            TradeType.Deposit when scope == PerformanceFlowScope.NetWorth =>
                trade.CashAmount ?? 0m,
            TradeType.Income when scope == PerformanceFlowScope.NetWorth =>
                trade.CashAmount ?? 0m,
            TradeType.Withdrawal when scope == PerformanceFlowScope.NetWorth =>
                -(trade.CashAmount ?? 0m),
            // 其餘（Transfer / Loan* / CreditCard* / StockDividend）皆不算
            _ => 0m,
        };
    }
}

/// <summary>
/// PerformanceFlowBuilder 的計算範圍。決定哪些 TradeType 算作 cash flow。
/// </summary>
public enum PerformanceFlowScope
{
    /// <summary>
    /// 預設：只考慮影響投資 MarketValue 的交易（Buy / Sell / CashDividend）。
    /// 對應 TWR 跑在 PortfolioDailySnapshot.MarketValue（投資組合市值）上的情境。
    /// </summary>
    InvestmentOnly = 0,

    /// <summary>
    /// 全資產淨值：加上 Deposit / Income / Withdrawal 等資金進出。
    /// 對應 TWR 跑在「淨值 = 現金 + 投資 − 負債」daily series 上的情境（待 daily NW
    /// snapshot 基建完成後啟用）。
    /// </summary>
    NetWorth = 1,
}
