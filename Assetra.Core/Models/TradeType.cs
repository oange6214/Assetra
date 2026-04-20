namespace Assetra.Core.Models;

public enum TradeType
{
    // ── 股票交易 ──────────────────────────────────────────────────
    Buy,             // 買入
    Sell,            // 賣出

    // ── 現金流入 ──────────────────────────────────────────────────
    Income,          // 現金收入（薪資 / 獎金 / 分紅 / 其他）
    CashDividend,    // 現金股利
    StockDividend,   // 股票股利（配股）

    // ── 帳戶操作 ──────────────────────────────────────────────────
    Deposit,         // 存入現金帳戶（外部資金進入）
    Withdrawal,      // 從現金帳戶提款（資金移出系統）
    Transfer,        // 帳戶間資金轉移（CashAccountId → ToCashAccountId）

    // ── 負債 / 貸款 ───────────────────────────────────────────────
    LoanBorrow,      // 借款（增加負債；LoanLabel 必填）
    LoanRepay,       // 還款（LoanLabel 必填；
                     //       Principal    = 本金部分，減少負債餘額；
                     //       InterestPaid = 利息部分，純費用不影響餘額）
}
