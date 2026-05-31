using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Goals;

/// <summary>
/// Portfolio-Groups-Refactor P5 — MVP <see cref="IGroupBalanceQueryService"/> 實作。
/// 把該 group 的所有 trade 用「簽名現金流」加總（與 BalanceQueryService 的
/// PrimaryCashDelta 同構），給 Hero 顯示 Goal 進度。
///
/// <para><b>MVP 限制 / 後續 phase：</b></para>
/// <list type="bullet">
///   <item>未做 mark-to-market：持有中部位以累計買入成本（負）+ 賣出收入（正）呈現，
///         不反映漲跌。完整實作需要 PortfolioEntry → group 對映與 quote 查詢。</item>
///   <item>未對應 cash 帳戶餘額。AssetItem(Cash) 雖然 schema 已加 portfolio_group_id 欄，
///         模型尚未讀出；之後升級為「持倉淨值 + cash 帳戶餘額」。</item>
/// </list>
/// </summary>
public sealed class GroupBalanceQueryService : IGroupBalanceQueryService
{
    private readonly ITradeRepository _tradeRepository;

    public GroupBalanceQueryService(ITradeRepository tradeRepository)
    {
        _tradeRepository = tradeRepository ?? throw new ArgumentNullException(nameof(tradeRepository));
    }

    public async Task<decimal> ComputeNetValueAsync(Guid groupId, CancellationToken ct = default)
    {
        var trades = await _tradeRepository.GetAllAsync(ct).ConfigureAwait(false);
        decimal total = 0m;
        foreach (var t in trades)
        {
            // null group_id 視為 DefaultGroup（schema migration backfill 保證新 row 都有值，
            // 但 null check 是防禦：避免讀到未跑 backfill 的 row 而漏算 default group）。
            var tradeGroup = t.PortfolioGroupId ?? PortfolioGroup.DefaultId;
            if (tradeGroup != groupId)
                continue;
            total += SignedCashDelta(t);
        }
        return total;
    }

    internal static decimal SignedCashDelta(Trade t) => t.Type switch
    {
        TradeType.Income or TradeType.Deposit or TradeType.LoanBorrow => +(t.CashAmount ?? 0m),
        TradeType.CashDividend => +(t.CashAmount ?? (t.Price * t.Quantity)),
        TradeType.Sell => +(t.CashAmount ?? (t.Price * t.Quantity - (t.Commission ?? 0m))),
        TradeType.CreditCardPayment => +(t.CashAmount ?? 0m),
        TradeType.Buy => -(t.CashAmount ?? (t.Price * t.Quantity + (t.Commission ?? 0m))),
        TradeType.Withdrawal or TradeType.LoanRepay or TradeType.CreditCardCharge => -(t.CashAmount ?? 0m),
        TradeType.Transfer => 0m,  // group-to-group 移轉視為 net 0；之後 P3+ 處理跨 group。
        _ => 0m,  // StockDividend / 其他不影響現金。
    };
}
