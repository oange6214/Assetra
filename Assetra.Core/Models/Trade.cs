namespace Assetra.Core.Models;

/// <summary>
/// Immutable record of a single financial transaction.
///
/// ─── 股票交易 ────────────────────────────────────────────────────────────
/// Buy / Sell:
///   Price    = 每股成交價
///   Quantity = 成交股數
///   RealizedPnl / RealizedPnlPct = 僅 Sell 時填入
///   CashAmount = 實際現金進出金額（Buy 扣款、Sell 入帳；可不同於標的幣別成交總額）
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
    decimal? CashAmount = null,          // 實際現金進出金額（收入 / 股利 / 買賣扣入款 / 轉帳 …）
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
    Guid? RecurringSourceId = null,      // 來源訂閱 / 週期交易（RecurringTransaction.Id）
                                         // ── 多幣別交易支援（MultiCurrency-Trade-Refactor P1）────────────
    /// <summary>
    /// 標的計價幣別（ISO 4217）。<see cref="Price"/> 與 <see cref="Commission"/>
    /// (when <see cref="CommissionCurrency"/> is null) 都以此幣別計價。
    /// 一律對齊 <see cref="Currency"/> 物件的 Code 寫法（"TWD"/"USD"/"JPY"/"HKD"/"EUR"）。
    /// 預設 "TWD" 是為了讓既有資料在加欄位但尚未 backfill 時仍有可解讀的語意；
    /// P2 backfill 會依 Exchange 把外幣標的改成對應幣別。
    /// </summary>
    string InstrumentCurrency = "TWD",
    /// <summary>
    /// 手續費的計價幣別。null 代表跟 <see cref="InstrumentCurrency"/> 一致
    /// （多數情境）。複委託有時手續費是以本幣（TWD）計算而非標的幣別（USD），
    /// 才會用到這個欄位。</summary>
    string? CommissionCurrency = null,
    /// <summary>
    /// 標的幣別 → 扣款帳戶幣別 的匯率（<c>1 InstrumentCcy = FxRate FundingCcy</c>）。
    /// null 代表兩個幣別相同（implicit 1.0），不需要 FX 轉換。
    /// 跨幣別交易（複委託、外幣標的）必須填，否則
    /// <see cref="CashAmount"/> 無法跟 <see cref="Price"/> × <see cref="Quantity"/> 對得起來。</summary>
    decimal? FxRate = null,
    /// <summary>
    /// 實際扣款 / 入帳的現金幣別（ISO 4217）。通常等於現金帳戶幣別。
    /// 與 <see cref="InstrumentCurrency"/> 不同時，<see cref="FxRate"/> 描述兩者換算關係。
    /// </summary>
    string SettlementCurrency = "TWD",
    /// <summary>
    /// <see cref="FxRate"/> 對應的有效日期。null 代表同幣別或使用者尚未提供可稽核日期。
    /// </summary>
    DateOnly? FxRateDate = null,
    /// <summary>
    /// <see cref="FxRate"/> 的來源，例如台灣銀行、broker statement 或 manual。
    /// </summary>
    string? FxSource = null,
    // ── Portfolio-Groups-Refactor P1 ─────────────────────────────────
    /// <summary>
    /// 所屬投資組合群組（bucket，如「退休帳戶」「買房儲蓄」）。
    /// null 代表尚未指派 — repository 在持久化時會替換成 <see cref="PortfolioGroup.DefaultId"/>，
    /// 已存在的舊資料則由 schema migration backfill 補上 DefaultId。
    /// 之後 UI / workflow 會把這個欄位暴露給使用者選擇。
    /// </summary>
    Guid? PortfolioGroupId = null,
    // ── MultiCurrency-Reporting P4.5b ─────────────────────────────────
    /// <summary>
    /// 已實現損益的「市場部分」（投資判斷貢獻）— base currency 計價。
    /// <c>= (sell_price - buy_avg_price) × qty × sell_fx_rate</c>。
    /// 同幣別交易時等同於 <see cref="RealizedPnl"/>。null = 賣出時 FX history
    /// 缺資料或 Buy 端未指派，無法計算；UI 顯示「—」。
    /// </summary>
    decimal? RealizedMarketPnl = null,
    /// <summary>
    /// 已實現損益的「FX 部分」（匯率漂移貢獻）— base currency 計價。
    /// <c>= buy_cost_native × (sell_fx_rate - buy_fx_rate)</c>。
    /// 同幣別交易為 0。null = 無法計算（同 <see cref="RealizedMarketPnl"/>）。
    /// </summary>
    decimal? RealizedFxPnl = null);
