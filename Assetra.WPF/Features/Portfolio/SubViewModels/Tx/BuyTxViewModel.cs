using System.Globalization;
using Assetra.Application.Fx;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio.SubViewModels.Tx;

/// <summary>
/// H1 — first child VM split off from <c>TransactionDialogViewModel</c>. Owns the
/// **buy transaction state cluster**:
/// <list type="bullet">
///   <item><see cref="AssetType"/>: stock / fund / metal / bond / crypto</item>
///   <item><see cref="MetaOnly"/>: watch-list mode (skip Buy trade row, Qty=0 position)</item>
///   <item><see cref="PriceMode"/>: unit-price vs total-amount input</item>
///   <item><see cref="TotalCost"/> + <see cref="TotalIncludesFee"/>: total-amount fields</item>
///   <item>Validation error string + computed display helpers</item>
/// </list>
///
/// <para>
/// Phase 2: state moves here, dialog VM keeps facade properties so XAML / tests stay green.
/// Phase 3: XAML migrated to <c>Buy.X</c> bindings.
/// Phase 4: facades deleted from dialog VM, completing the cluster split.
/// </para>
/// </summary>
public sealed partial class BuyTxViewModel : ObservableObject
{
    /// <summary>Asset category — drives which input panel the form shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStock))]
    [NotifyPropertyChangedFor(nameof(IsNonStock))]
    [NotifyPropertyChangedFor(nameof(IsCrypto))]
    private string _assetType = "stock";

    /// <summary>
    /// Watchlist-only mode — when true, ConfirmBuy writes only the PortfolioEntry
    /// and skips the Buy trade row, producing a Qty=0 position with no balance impact.
    /// (Plan Task 18 — Option B.)
    /// </summary>
    [ObservableProperty] private bool _metaOnly;

    /// <summary>"unit" = user enters per-share price; "total" = user enters total amount.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnitMode))]
    [NotifyPropertyChangedFor(nameof(IsTotalMode))]
    private string _priceMode = "unit";

    /// <summary>「總額」模式下使用者輸入的總成交金額。</summary>
    [ObservableProperty] private string _totalCost = string.Empty;

    /// <summary>
    /// 「總額」模式下，輸入的總額是否已包含手續費。預設 true（多數券商
    /// 給的成交回報是含手續費的最終扣款金額）。
    /// </summary>
    [ObservableProperty] private bool _totalIncludesFee = true;

    /// <summary>正/負數驗證錯誤訊息（空字串 = 通過）。</summary>
    [ObservableProperty] private string _totalCostError = string.Empty;

    /// <summary>
    /// Optional broker-confirmed cash debit, in the linked cash account currency.
    /// Used for sub-brokerage / FX settlement where price × shares in the
    /// instrument currency is not the exact cash-account deduction.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Premium))]
    [NotifyPropertyChangedFor(nameof(ShowPremium))]
    [NotifyPropertyChangedFor(nameof(PremiumPercentDisplay))]
    [NotifyPropertyChangedFor(nameof(PremiumGrade))]
    private string _actualCashAmount = string.Empty;

    /// <summary>
    /// 成交價金（以成交／標的幣別計，＝數量 × 每股價格），由 <c>TransactionDialogViewModel</c>
    /// 在數量或價格變動時同步寫入。<b>僅供溢價合理性檢查使用</b>，不參與任何寫入路徑。
    /// null＝資料還不足以檢查。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Premium))]
    [NotifyPropertyChangedFor(nameof(ShowPremium))]
    [NotifyPropertyChangedFor(nameof(PremiumPercentDisplay))]
    [NotifyPropertyChangedFor(nameof(PremiumGrade))]
    private decimal? _grossNative;

    /// <summary>Validation error for <see cref="ActualCashAmount"/>.</summary>
    [ObservableProperty] private string _actualCashAmountError = string.Empty;

    /// <summary>
    /// Cross-currency settlement input authority:
    /// "statement" means the broker/account statement cash amount is authoritative;
    /// "fx" means the FX rate is authoritative and cash amount is estimated.
    /// </summary>
    /// <remarks>
    /// 預設 "fx"（依匯率估算）：新增交易對話框已移除「帳戶扣款」卡，沒有實際扣款可輸入，
    /// 台幣扣款一律由 價金 × 自動抓的當日市場匯率 ＋ 手續費 推得。若仍預設 "statement"，
    /// 確認時會要求一個介面上根本不存在的欄位而卡死。匯入券商明細／事後校正等有真實金額的
    /// 路徑仍可把此值設回 "statement"，確認層兩條分支都原封不動。
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatementSettlementMode))]
    [NotifyPropertyChangedFor(nameof(IsFxSettlementMode))]
    [NotifyPropertyChangedFor(nameof(Premium))]
    [NotifyPropertyChangedFor(nameof(ShowPremium))]
    [NotifyPropertyChangedFor(nameof(PremiumPercentDisplay))]
    [NotifyPropertyChangedFor(nameof(PremiumGrade))]
    private string _settlementInputMode = "fx";

    /// <summary>
    /// 跨幣別交易的匯率（標的幣別 → 扣款帳戶幣別）。
    /// 例：標的 USD、帳戶 TWD，FxRate = 31.5 表示 1 USD = 31.5 TWD。
    /// 同幣別交易留空字串（後續由 <see cref="ActualCashAmount"/> 反推或保持 implicit 1.0）。
    /// MultiCurrency-Trade-Refactor P3 — 跨幣別 Mode 才暴露此欄位。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Premium))]
    [NotifyPropertyChangedFor(nameof(ShowPremium))]
    [NotifyPropertyChangedFor(nameof(PremiumPercentDisplay))]
    [NotifyPropertyChangedFor(nameof(PremiumGrade))]
    private string _fxRate = string.Empty;

    /// <summary>Validation error for <see cref="FxRate"/>.</summary>
    [ObservableProperty] private string _fxRateError = string.Empty;

    // ── 總成本溢價合理性檢查（顯示用，不影響寫入）───────────────────────────
    //
    // 只在「依帳戶明細」模式有意義：那時 ActualCashAmount 是使用者獨立輸入的實際扣款，
    // 跟「價金 × 市場匯率」比對才有資訊量。「依匯率估算」模式的現金本來就是用匯率推出來的
    // （cash = gross × fx），比對必然為 0，故不顯示。
    //
    // FxRate 在此當作市場匯率基準：跨幣別時由 TransactionFxRateResolver 依交易日自動帶入
    // （查 fx_rate_history）。使用者手動覆寫過也照用——那代表他主張的匯率，仍值得對照。
    // 任一項缺漏（含尚未取得匯率）→ Evaluate 回 null → 不顯示，絕不擋記帳。

    /// <summary>溢價評估結果；null＝資料不足或非適用情境，不做檢查。</summary>
    public SettlementPremiumResult? Premium =>
        IsCrossCurrency && IsStatementSettlementMode
            ? SettlementPremiumCalculator.Evaluate(
                GrossNative,
                ParseHelpers.TryParseDecimal(ActualCashAmount, out var cash) ? cash : null,
                ParseHelpers.TryParseDecimal(FxRate, out var fx) ? fx : null)
            : null;

    /// <summary>是否顯示溢價提示列。</summary>
    public bool ShowPremium => Premium is not null;

    /// <summary>分級（Normal / Warning / Excessive），供 UI 決定顏色與措辭。</summary>
    public SettlementPremiumGrade PremiumGrade => Premium?.Grade ?? SettlementPremiumGrade.Normal;

    /// <summary>帶正負號的百分比字串，例：+1.6% / −12.3%。</summary>
    public string PremiumPercentDisplay =>
        Premium is { } p ? p.Percent.ToString("+0.0%;-0.0%;0.0%") : string.Empty;

    /// <summary>
    /// 標的計價幣別（ISO 4217）。由 <c>TransactionDialogViewModel</c> 依照
    /// 使用者選的 Symbol / Exchange 即時同步。為空字串時 UI 不顯示「USD/股」這類 badge。
    /// </summary>
    [ObservableProperty] private string _instrumentCurrency = string.Empty;

    /// <summary>
    /// 選中的扣款帳戶幣別。由 <c>TransactionDialogViewModel</c> 依照
    /// <c>TxCashAccount.Currency</c> 同步寫入。為空字串時視為 "TWD"。
    /// </summary>
    [ObservableProperty] private string _cashAccountCurrency = string.Empty;

    /// <summary>實際扣款 / 入帳的現金幣別，預設跟現金帳戶幣別一致。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettlementCurrencyDisplay))]
    [NotifyPropertyChangedFor(nameof(SettlementPairDisplay))]
    private string _settlementCurrency = string.Empty;

    /// <summary>匯率實際採用的日期；可能是交易日，也可能是最近可用的歷史匯率日。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FxRateDateDisplay))]
    private DateOnly? _fxRateDate;

    /// <summary>匯率來源名稱，例如台灣銀行；空字串代表尚未查得或使用者手動輸入。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FxSourceDisplay))]
    private string _fxSourceLabel = string.Empty;

    /// <summary>True 代表使用者手動覆寫匯率，日期/幣別變動時不自動蓋掉。</summary>
    [ObservableProperty] private bool _isFxManual;

    // IsAdvancedExpanded 已移除：買入表單的「進階」Expander 整個拿掉了（最後只剩一個選填
    // 手續費欄位還包一張卡片，純屬多餘），手續費已並排到成交明細列。賣出表單仍有自己的
    // Expander，故 SellTxViewModel 保留同名屬性。

    /// <summary>匯率查詢失敗或缺資料的可見錯誤訊息。</summary>
    [ObservableProperty] private string _fxFetchError = string.Empty;

    /// <summary>
    /// True 當 <see cref="InstrumentCurrency"/> 與 <see cref="CashAccountCurrency"/> 不同。
    /// XAML 用此屬性決定要不要顯示 FX rate 欄位與「跨幣別」hint。
    /// 空字串視為 "TWD"，避免初始尚未選定時誤判。
    /// </summary>
    public bool IsCrossCurrency
    {
        get
        {
            var instr = string.IsNullOrWhiteSpace(InstrumentCurrency) ? "TWD" : InstrumentCurrency.Trim().ToUpperInvariant();
            var cash = NormalizeSettlementCurrency();
            return !string.Equals(instr, cash, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// <see cref="IsCrossCurrency"/> 變動時一併刷新溢價相關屬性（它們以 IsCrossCurrency 為前提）。
    /// 各 On*CurrencyChanged 已負責 raise IsCrossCurrency，這裡集中處理其連動項。
    /// </summary>
    private void NotifyPremiumChanged()
    {
        OnPropertyChanged(nameof(Premium));
        OnPropertyChanged(nameof(ShowPremium));
        OnPropertyChanged(nameof(PremiumPercentDisplay));
        OnPropertyChanged(nameof(PremiumGrade));
    }

    partial void OnInstrumentCurrencyChanged(string value)
    {
        OnPropertyChanged(nameof(IsCrossCurrency));
        OnPropertyChanged(nameof(InstrumentCurrencyBadge));
        OnPropertyChanged(nameof(SettlementPairDisplay));
        NotifyPremiumChanged();
    }

    partial void OnCashAccountCurrencyChanged(string value)
    {
        SettlementCurrency = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();
        OnPropertyChanged(nameof(IsCrossCurrency));
        OnPropertyChanged(nameof(SettlementPairDisplay));
        NotifyPremiumChanged();
    }

    partial void OnSettlementInputModeChanged(string value)
    {
        var normalized = string.Equals(value, "fx", StringComparison.OrdinalIgnoreCase)
            ? "fx"
            : "statement";
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
            SettlementInputMode = normalized;
    }

    partial void OnSettlementCurrencyChanged(string value)
    {
        OnPropertyChanged(nameof(IsCrossCurrency));
        OnPropertyChanged(nameof(SettlementPairDisplay));
        NotifyPremiumChanged();
    }

    private string NormalizeSettlementCurrency() =>
        !string.IsNullOrWhiteSpace(SettlementCurrency)
            ? SettlementCurrency.Trim().ToUpperInvariant()
            : !string.IsNullOrWhiteSpace(CashAccountCurrency)
                ? CashAccountCurrency.Trim().ToUpperInvariant()
                : "TWD";

    /// <summary>
    /// 顯示在「成交價」label 旁的小幣別 badge。
    /// 空字串 / TWD 時回傳空字串 → UI 不渲染 badge（無干擾）。
    /// 其他幣別回傳例如「(USD / 股)」讓使用者一眼看出 Price 計價單位。
    /// MultiCurrency-Trade-Refactor P3。
    /// </summary>
    public string InstrumentCurrencyBadge
    {
        get
        {
            if (string.IsNullOrWhiteSpace(InstrumentCurrency))
                return string.Empty;
            var c = InstrumentCurrency.Trim().ToUpperInvariant();
            return c == "TWD" ? string.Empty : $"({c} / 股)";
        }
    }

    public string SettlementCurrencyDisplay => NormalizeSettlementCurrency();

    public string SettlementPairDisplay
    {
        get
        {
            var instr = string.IsNullOrWhiteSpace(InstrumentCurrency)
                ? "TWD"
                : InstrumentCurrency.Trim().ToUpperInvariant();
            return $"{instr} → {NormalizeSettlementCurrency()}";
        }
    }

    public string FxRateDateDisplay =>
        FxRateDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";

    public string FxSourceDisplay =>
        string.IsNullOrWhiteSpace(FxSourceLabel) ? "—" : FxSourceLabel;

    public bool IsStock => AssetType == "stock";
    public bool IsNonStock => AssetType is "fund" or "metal" or "bond";
    public bool IsCrypto => AssetType == "crypto";

    public bool IsUnitMode => PriceMode == "unit";
    public bool IsTotalMode => PriceMode == "total";
    public bool IsStatementSettlementMode => SettlementInputMode == "statement";
    public bool IsFxSettlementMode => SettlementInputMode == "fx";

    // ComputeTotalDisplay 已移除：它只服務 TransactionDialogViewModel.TxBuyComputedTotalDisplay
    // 這個「總計鏡像」文字，而鏡像已被可編輯的成交總額欄位取代。

    /// <summary>重置所有買入欄位回預設（dialog 開新交易時呼叫）。</summary>
    public void Reset()
    {
        AssetType = "stock";
        MetaOnly = false;
        PriceMode = "unit";
        TotalCost = string.Empty;
        TotalIncludesFee = true;  // most broker totals include fee
        TotalCostError = string.Empty;
        ActualCashAmount = string.Empty;
        ActualCashAmountError = string.Empty;
        GrossNative = null;
        SettlementInputMode = "fx";
        // P3
        FxRate = string.Empty;
        FxRateError = string.Empty;
        InstrumentCurrency = string.Empty;
        CashAccountCurrency = string.Empty;
        SettlementCurrency = string.Empty;
        FxRateDate = null;
        FxSourceLabel = string.Empty;
        IsFxManual = false;
        FxFetchError = string.Empty;
    }
}
