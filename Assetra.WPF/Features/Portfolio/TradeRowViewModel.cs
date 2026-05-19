using Assetra.Core.Models;
using Assetra.WPF.Infrastructure.Converters;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>Display ViewModel for a single historical trade row.</summary>
public sealed class TradeRowViewModel : ObservableObject
{
    public Guid Id { get; }
    public string Symbol { get; }
    public string Name { get; }
    public TradeType Type { get; }
    public DateTime TradeDate { get; }

    /// <summary>TradeDate converted to local time for UI display.</summary>
    public DateTime TradeDateLocal => TradeDate.ToLocalTime();
    public decimal Price { get; }
    public int Quantity { get; }
    public decimal? RealizedPnl { get; }
    public decimal? RealizedPnlPct { get; }
    /// <summary>
    /// MultiCurrency-Reporting P4.5b — market component of realized PnL
    /// (stock pick contribution) in base currency. Same as <see cref="RealizedPnl"/>
    /// for same-currency trades. Null = breakdown unavailable (UI shows "—").
    /// </summary>
    public decimal? RealizedMarketPnl { get; }
    /// <summary>
    /// MultiCurrency-Reporting P4.5b — FX component of realized PnL
    /// (currency drift contribution) in base currency. Zero for same-currency
    /// trades. Null = breakdown unavailable.
    /// </summary>
    public decimal? RealizedFxPnl { get; }
    /// <summary>True when the breakdown is computed and meaningful (mixed-currency).
    /// UI uses this to decide whether to show the breakdown columns vs hyphen.</summary>
    public bool HasRealizedPnlBreakdown =>
        RealizedMarketPnl.HasValue && RealizedFxPnl.HasValue;
    public decimal? CashAmount { get; }
    /// <summary>Buy: brokerage commission. Sell: commission + transaction tax. Null for legacy or non-stock trades.</summary>
    public decimal? Commission { get; }
    /// <summary>
    /// 建立當下使用的手續費折扣（0.1~1.0）。null = 使用者以「手續費（選填）」手動覆蓋。
    /// 編輯對話框用此值還原 UI 狀態。
    /// </summary>
    public decimal? CommissionDiscount { get; }
    /// <summary>Linked cash-account id (Income / Deposit / Withdrawal / CashDividend / LoanBorrow / LoanRepay / Transfer).</summary>
    public Guid? CashAccountId { get; }
    /// <summary>
    /// Linked portfolio-lot id for Buy / Sell / StockDividend trades. Null on cash / loan
    /// trades, and also on legacy Buy trades predating the link column.
    /// </summary>
    public Guid? PortfolioEntryId { get; }
    public string? Note { get; }

    // ── 負債欄位（LoanBorrow / LoanRepay）────────────────────────────────
    /// <summary>LoanBorrow / LoanRepay 關聯的貸款標籤。舊版記錄此欄為 null。</summary>
    public string? LoanLabel { get; }
    /// <summary>LoanRepay：本金部分（減少負債餘額）。舊版記錄為 null → 視全額為本金。</summary>
    public decimal? Principal { get; }
    /// <summary>LoanRepay：利息部分（費用支出，不影響負債餘額）。</summary>
    public decimal? InterestPaid { get; }
    // ── 轉帳欄位（Transfer）──────────────────────────────────────────────
    /// <summary>Transfer：目標現金帳戶。</summary>
    public Guid? ToCashAccountId { get; }
    /// <summary>信用卡等負債資產 Id。</summary>
    public Guid? LiabilityAssetId { get; }

    /// <summary>
    /// MultiCurrency-Trade-Refactor P3 — 標的計價幣別 (ISO 4217)。預設 "TWD"，
    /// 既有 row 從 DB 預設值繼承。Edit-trade 流程用此值還原 BuyTxViewModel.InstrumentCurrency。
    /// </summary>
    public string InstrumentCurrency { get; }

    /// <summary>
    /// P3 — 跨幣別交易匯率（標的 → 帳戶幣別）。null = 同幣別 / implicit 1.0。
    /// </summary>
    public decimal? FxRate { get; }

    /// <summary>
    /// Portfolio-Groups-Refactor P3 — 此筆交易所屬群組（bucket）。
    /// null 代表 legacy row 尚未 backfill；UI 視為 DefaultId。
    /// </summary>
    public Guid? PortfolioGroupId { get; }

    // Type predicates — used by XAML DataTriggers and ViewModel filter queries.
    // Intentionally verbose (one-per-TradeType) rather than a single enum switch so that
    // binding {Binding IsBuy} / {Binding IsCashDividend} stays cheap and self-documenting.
    public bool IsBuy => Type == TradeType.Buy;
    public bool IsSell => Type == TradeType.Sell;
    public bool IsIncome => Type == TradeType.Income;
    public bool IsCashDividend => Type == TradeType.CashDividend;
    public bool IsDividend => Type is TradeType.CashDividend or TradeType.StockDividend;
    public bool IsDeposit => Type == TradeType.Deposit;
    public bool IsWithdrawal => Type == TradeType.Withdrawal;
    public bool IsLoanBorrow => Type == TradeType.LoanBorrow;
    public bool IsLoanRepay => Type == TradeType.LoanRepay;
    public bool IsCreditCardCharge => Type == TradeType.CreditCardCharge;
    public bool IsCreditCardPayment => Type == TradeType.CreditCardPayment;
    public bool IsTransfer => Type == TradeType.Transfer;

    // Display helpers
    // TypeLabel removed — use TradeTypeConverter in XAML for localized display.
    //
    // Column model (matches AscentPortfolio-style unified trade log):
    //   代號   → DisplaySymbol        (empty for non-stock types — avoids ticker-styled account names)
    //   單價   → Price + price-dash   (per-share; dash for loan/cashflow/income)
    //   數量   → DisplayQuantityText  (dash for loan/cashflow/income)
    //   總額   → TotalAmount + amount-dash (Price × Qty for stock trades; CashAmount for cash types)

    /// <summary>
    /// Symbol shown in the trade list; returns empty string for non-stock transaction types
    /// (loan, deposit, withdrawal, income) to avoid showing the account name in the
    /// stock-ticker–styled column.
    /// </summary>
    public string DisplaySymbol => Type is TradeType.Buy or TradeType.Sell
        or TradeType.CashDividend or TradeType.StockDividend
        ? Symbol : string.Empty;

    /// <summary>
    /// Unified asset descriptor shown in the trade list (AscentPortfolio-style).
    /// <list type="bullet">
    /// <item><description>Stocks &amp; dividends (Buy / Sell / CashDiv / StockDiv): <c>"Name (Symbol)"</c>
    /// when both differ, e.g. <c>"主動群益台灣強棒 (00982A)"</c></description></item>
    /// <item><description>Cash flow / loan / interest: just the account or descriptive name
    /// (e.g. <c>"台新A 7y"</c>, <c>"薪資"</c>, <c>"利息支出"</c>) — no bracketed suffix because
    /// Symbol equals Name in those cases.</description></item>
    /// </list>
    /// </summary>
    public string DisplayAsset
    {
        get
        {
            // Stock-related types may have distinct symbol + name → combine
            if (Type is TradeType.Buy or TradeType.Sell
                     or TradeType.CashDividend or TradeType.StockDividend)
            {
                if (!string.IsNullOrWhiteSpace(Name) &&
                    !string.IsNullOrWhiteSpace(Symbol) &&
                    !string.Equals(Name, Symbol, StringComparison.Ordinal))
                {
                    return $"{Name} ({Symbol})";
                }
                // Fallback: only one is meaningful — prefer Name, then Symbol
                return string.IsNullOrWhiteSpace(Name) ? Symbol : Name;
            }
            // Cash flow / loan / income — Name carries the descriptor.
            return Name;
        }
    }

    /// <summary>
    /// Quantity text for display. Returns a formatted number for stock trades and dividends;
    /// returns an em-dash for cash-flow and loan types where quantity is not meaningful.
    /// </summary>
    public string DisplayQuantityText => Type is TradeType.Buy or TradeType.Sell
        or TradeType.CashDividend or TradeType.StockDividend
        ? Quantity.ToString("N0") : "—";

    /// <summary>
    /// 實際現金流動金額（用於 總額 欄）。符號一律依「現金實際進出」：流入為 +，流出為 −。
    /// 與 <c>BalanceQueryService.PrimaryCashDelta</c> 同構，因此
    /// <c>Σ TotalAmount</c>（來源帳戶視角）= 該帳戶餘額變動。
    /// <list type="bullet">
    /// <item><description>Buy → −(P×Q + Commission)（實付）</description></item>
    /// <item><description>Sell → +(P×Q − Commission)（實收）</description></item>
    /// <item><description>CashDividend → +CashAmount（若為 legacy 空值則回退 P×Q）</description></item>
    /// <item><description>Income / Deposit / LoanBorrow → +CashAmount（流入）</description></item>
    /// <item><description>Withdrawal / LoanRepay / Transfer → −CashAmount（流出）</description></item>
    /// <item><description>CreditCardCharge → −CashAmount（卡帳戶代你支付給商家）</description></item>
    /// <item><description>CreditCardPayment → +CashAmount（卡帳戶收到還款）</description></item>
    /// <item><description>StockDividend → 0（無現金；欄位以 amount/signed-dash converter 顯示 "—"）</description></item>
    /// </list>
    /// 徽章色 (<see cref="TypeBadgeColor"/>) 仍負責「型態識別」（Buy 綠 / Sell 紅 / 其他灰），
    /// 文字色由 <see cref="IsAmountPositive"/> 依符號決定，兩者職責分離。
    /// </summary>
    public decimal TotalAmount => Type switch
    {
        TradeType.Buy => -BuyCashAmount(),
        TradeType.Sell => +SellCashAmount(),
        TradeType.CashDividend => +(CashAmount ?? (Price * Quantity)),
        TradeType.Income or TradeType.Deposit or TradeType.LoanBorrow => +(CashAmount ?? 0),
        TradeType.Withdrawal or TradeType.LoanRepay or TradeType.Transfer => -(CashAmount ?? 0),
        // 信用卡列以「卡帳戶視角」呈現：消費＝卡幫你付出去（−）；繳款＝你還錢給卡（+）。
        TradeType.CreditCardCharge => -(CashAmount ?? 0),
        TradeType.CreditCardPayment => +(CashAmount ?? 0),
        _ => 0,   // StockDividend
    };

    private decimal BuyCashAmount() =>
        CashAmount ?? (Price * Quantity + (Commission ?? 0m));

    private decimal SellCashAmount() =>
        CashAmount ?? (Price * Quantity - (Commission ?? 0m));

    /// <summary>
    /// 「手續費」欄顯示文字。非 Buy/Sell、null 或 0 都顯示 —，確保欄位對齊且讓使用者
    /// 一眼分辨「無手續費資料」vs「確實 0 元」。透過 CurrencyConverter.Service 格式化
    /// 以跟隨使用者的貨幣偏好 (TWD / USD)。
    /// </summary>
    public string CommissionDisplay
    {
        get
        {
            if (Type is not (TradeType.Buy or TradeType.Sell))
                return "—";
            if (Commission is not { } com || com <= 0)
                return "—";
            var sign = Type == TradeType.Sell ? "-" : "+";
            return sign + (CurrencyConverter.Service?.FormatAmount(com) ?? com.ToString("N0"));
        }
    }

    /// <summary>Row 2 資訊是否顯示（Buy / Sell 才有成交金額 + 手續費 + 實付/實收三欄）。</summary>
    public bool HasTradeBreakdown => Type is TradeType.Buy or TradeType.Sell;

    /// <summary>
    /// True 代表金額為流入（綠色文字），False 代表流出（紅色文字）。
    /// 直接由 <see cref="TotalAmount"/> 的符號決定，語意上 = 「這筆帳戶餘額是增加還是減少」。
    /// StockDividend 的 TotalAmount = 0，此處回傳 true 但欄位實際顯示 "—"，不影響視覺。
    /// </summary>
    public bool IsAmountPositive => TotalAmount >= 0;

    /// <summary>
    /// Badge background color hex. Minimal-palette rule — only the stock trading actions
    /// keep their conventional green/red (buy / sell). Every other type uses a neutral
    /// slate gray so the badges read as labels, not signals; this avoids semantic conflicts
    /// like "borrow = red even though cash came in" and "repay = green even though cash
    /// went out".
    /// </summary>
    public string TypeBadgeColor => Type switch
    {
        TradeType.Buy => "#22C55E",  // 綠：買入（業界慣例）
        TradeType.Sell => "#EF4444",  // 紅：賣出（業界慣例）
        _ => "#64748B",  // 中性灰（slate-500）：現金流、借還款、股利、利息
    };

    /// <summary>
    /// True when this trade is one leg of a transfer (Withdrawal from source or Deposit to
    /// target). Detected via the Note prefix that <c>ConfirmTransferAsync</c> stamps.
    /// Editing a single leg would break the paired invariant (source withdrew N but target
    /// deposit unchanged, or vice versa), so these trades are meta-only until a proper
    /// paired-edit flow exists.
    /// </summary>
    public bool IsTransferLeg =>
        (Type == TradeType.Withdrawal || Type == TradeType.Deposit) &&
        Note is not null &&
        (Note.StartsWith("轉帳 →", StringComparison.Ordinal) ||
         Note.StartsWith("轉帳 ←", StringComparison.Ordinal));

    /// <summary>
    /// True when the trade's economic fields (symbol / price / quantity / cash amount) are
    /// <b>not</b> safe to direct-edit — only date and note can change. Caller (dialog VM)
    /// shows the locked-summary card + forces explicit 修訂 flow for these.
    /// <list type="bullet">
    /// <item><description><b>Sell</b>: realized P&amp;L is FIFO-computed and stored on the
    /// Trade row; editing in place would silently invalidate downstream P&amp;L. User must
    /// click 修訂 to consciously delete + create new.</description></item>
    /// <item><description><b>Cross-currency Transfer leg</b> (paired Withdrawal/Deposit with
    /// "轉帳 →"/"轉帳 ←" note prefix): the partner leg is a separate record without a FK
    /// link, so editing one would leave the other orphaned. Pair-aware deletion is a
    /// schema-level concern; until that lands, legs stay locked.</description></item>
    /// </list>
    /// <para>
    /// Buy / StockDividend WITHOUT <see cref="PortfolioEntryId"/> used to be in this list
    /// (the "legacy trade" case) but were relaxed — they unlock via implicit revision
    /// (delete-old + create-new). Trade row is source of truth; cost basis projects from
    /// the journal on next reload, so updating the Buy is safe.
    /// </para>
    /// <para>
    /// Native Transfer records (single trade with <see cref="ToCashAccountId"/>) ARE
    /// directly editable — ConfirmTx falls through to ConfirmTransferAsync which creates
    /// a fresh trade (or pair, if the user changed amounts to differ), and the post-success
    /// block deletes the single old record.
    /// </para>
    /// </summary>
    public bool IsMetaOnlyEditType => Type == TradeType.Sell || IsTransferLeg;

    /// <summary>
    /// 貨幣切換時由 PortfolioViewModel 呼叫，強制金額欄位重新通知。
    /// </summary>
    public void NotifyCurrencyChanged()
    {
        OnPropertyChanged(nameof(Price));
        OnPropertyChanged(nameof(RealizedPnl));
        OnPropertyChanged(nameof(CashAmount));
        OnPropertyChanged(nameof(TotalAmount));
    }

    public TradeRowViewModel(Trade t)
    {
        Id = t.Id;
        Symbol = t.Symbol;
        Name = t.Name;
        Type = t.Type;
        TradeDate = t.TradeDate;
        Price = t.Price;
        Quantity = t.Quantity;
        RealizedPnl = t.RealizedPnl;
        RealizedPnlPct = t.RealizedPnlPct;
        RealizedMarketPnl = t.RealizedMarketPnl;
        RealizedFxPnl = t.RealizedFxPnl;
        CashAmount = t.CashAmount;
        Commission = t.Commission;
        CommissionDiscount = t.CommissionDiscount;
        CashAccountId = t.CashAccountId;
        PortfolioEntryId = t.PortfolioEntryId;
        Note = t.Note;
        LoanLabel = t.LoanLabel;
        Principal = t.Principal;
        InterestPaid = t.InterestPaid;
        ToCashAccountId = t.ToCashAccountId;
        LiabilityAssetId = t.LiabilityAssetId;
        InstrumentCurrency = t.InstrumentCurrency;
        FxRate = t.FxRate;
        PortfolioGroupId = t.PortfolioGroupId;
    }
}
