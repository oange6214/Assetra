namespace Assetra.Core.Models;

/// <summary>
/// Immutable record of a single financial transaction.
///
/// ─── 股票交易 ────────────────────────────────────────────────────────────
/// Buy / Sell:
///   Price    = 每股成交價
///   Quantity = 成交股數
///   RealizedPnl / RealizedPnlPct = 僅 Sell 時填入
///   CashAccountId = 扣款帳戶（Buy）或入帳帳戶（Sell）
///
/// ─── 股利 ────────────────────────────────────────────────────────────────
/// CashDividend:
///   Price      = 每股股利
///   Quantity   = 除息日持股數
///   CashAmount = Price × Quantity
///   CashAccountId = 股利入帳的現金帳戶
///
/// StockDividend:
///   Price    = 0（無成本）
///   Quantity = 配得股數
///   CashAmount = null
///
/// ─── 現金流入 ─────────────────────────────────────────────────────────────
/// Income: 現金收入（薪資 / 獎金 / 分紅 …）
///   Price = 0, Quantity = 1
///   CashAmount    = 收入金額
///   CashAccountId = 入帳現金帳戶
///   Note          = 收入類型標籤（"薪資" / "獎金" / …）
///
/// ─── 帳戶操作 ─────────────────────────────────────────────────────────────
/// Deposit:
///   CashAmount    = 存入金額
///   CashAccountId = 目標現金帳戶
///
/// Withdrawal:
///   CashAmount    = 提款金額
///   CashAccountId = 來源現金帳戶
///
/// Transfer: 現金帳戶間資金轉移
///   CashAmount      = 轉帳金額
///   CashAccountId   = 來源現金帳戶
///   ToCashAccountId = 目標現金帳戶
///
/// ─── 負債 / 貸款 ──────────────────────────────────────────────────────────
/// LoanBorrow: 新增借款，增加負債餘額
///   CashAmount        = 借款金額（入帳至現金帳戶）
///   CashAccountId     = 借款撥入的現金帳戶
///   LoanLabel = 貸款名稱（例如 "國泰信貸"）
///
/// LoanRepay: 還款，減少負債餘額並記錄利息支出
///   CashAmount        = 本次付款總額（Principal + InterestPaid）
///   CashAccountId     = 扣款的現金帳戶
///   LoanLabel = 貸款名稱（負債餘額 -= Principal）
///   Principal         = 本金部分（減少負債餘額）
///   InterestPaid      = 利息部分（費用支出，不影響負債餘額）
///
/// CreditCardCharge: 信用卡消費，增加信用卡未繳餘額
///   CashAmount        = 消費金額
///   LiabilityAssetId  = 對應的信用卡負債資產 Id
///
/// CreditCardPayment: 信用卡繳款，減少信用卡未繳餘額
///   CashAmount        = 繳款金額
///   CashAccountId     = 扣款的現金帳戶
///   LiabilityAssetId  = 對應的信用卡負債資產 Id
///
/// ─── 關聯欄位 ─────────────────────────────────────────────────────────────
/// CashAccountId: 任何涉及現金移動的交易都應設定；
///   Transfer 同時設定 ToCashAccountId。
///
/// PortfolioEntryId: 連結 Buy / Sell / StockDividend 回所屬持倉批次
///   (<see cref="PortfolioEntry"/>)。現金 / 負債 / 收入類交易為 null。
///   啟動時會試圖回填舊版缺少此欄的 Buy 記錄。
///
/// LoanLabel: LoanBorrow / LoanRepay 的貸款名稱（如 "國泰信貸"）；其他類型為 null。
/// LiabilityAssetId: 連結到負債資產（目前主要給信用卡使用）；一般交易為 null。
/// </summary>
public sealed record Trade(
    Guid Id,
    string Symbol,
    string Exchange,
    string Name,
    TradeType Type,
    DateTime TradeDate,
    decimal Price,
    int Quantity,
    decimal? RealizedPnl,
    decimal? RealizedPnlPct,
    decimal? CashAmount = null,          // 現金金額（收入 / 股利 / 存款 / 轉帳 …）
    Guid? CashAccountId = null,          // 來源或主要現金帳戶
    string? Note = null,                 // 收入類型標籤或備註
    Guid? PortfolioEntryId = null,       // 所屬持倉批次（Buy / Sell / StockDividend）
    decimal? Commission = null,          // 手續費
    /// <summary>
    /// 建立該筆交易時使用的手續費折扣（0.1 ~ 1.0）。
    /// 為 null 代表使用者透過「手續費（選填）」手動覆蓋，沒有走折扣計算路徑。
    /// 用於編輯交易時還原對話框原始狀態，避免折扣／手動兩者誤判。
    /// </summary>
    decimal? CommissionDiscount = null,
    // ── 負債欄位（LoanBorrow / LoanRepay）────────────────────────
    string? LoanLabel = null,            // 借款/還款的貸款名稱（例如 "國泰信貸"）
    decimal? Principal = null,           // LoanRepay：本金部分（減少負債餘額）
    decimal? InterestPaid = null,        // LoanRepay：利息部分（費用，不減餘額）
    // ── 轉帳欄位（Transfer）──────────────────────────────────────
    Guid? ToCashAccountId = null,        // Transfer：目標現金帳戶
    // ── 負債資產連結──────────────────────────────────────────────
    Guid? LiabilityAssetId = null,       // 信用卡等負債資產 Id
    // ── 子記錄連結──────────────────────────────────────────────
    /// <summary>
    /// 若本筆是另一筆主交易的附屬費用子記錄（例如手續費 Withdrawal），
    /// 此欄位指向主交易的 <see cref="Id"/>。主交易刪除時應連帶刪除所有子記錄。
    /// 一般交易為 null。
    /// </summary>
    Guid? ParentTradeId = null,          // 主交易 Id（費用子記錄使用）
    // ── 收支分類 / 週期交易來源（P1 收支管理）────────────────────
    Guid? CategoryId = null,             // 收支分類（ExpenseCategory.Id）
    Guid? RecurringSourceId = null);     // 來源訂閱 / 週期交易（RecurringTransaction.Id）
