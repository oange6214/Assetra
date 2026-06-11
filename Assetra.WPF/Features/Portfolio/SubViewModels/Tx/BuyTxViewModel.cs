using System.Globalization;
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
    [ObservableProperty] private string _actualCashAmount = string.Empty;

    /// <summary>Validation error for <see cref="ActualCashAmount"/>.</summary>
    [ObservableProperty] private string _actualCashAmountError = string.Empty;

    /// <summary>
    /// Cross-currency settlement input authority:
    /// "statement" means the broker/account statement cash amount is authoritative;
    /// "fx" means the FX rate is authoritative and cash amount is estimated.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatementSettlementMode))]
    [NotifyPropertyChangedFor(nameof(IsFxSettlementMode))]
    private string _settlementInputMode = "statement";

    /// <summary>
    /// 跨幣別交易的匯率（標的幣別 → 扣款帳戶幣別）。
    /// 例：標的 USD、帳戶 TWD，FxRate = 31.5 表示 1 USD = 31.5 TWD。
    /// 同幣別交易留空字串（後續由 <see cref="ActualCashAmount"/> 反推或保持 implicit 1.0）。
    /// MultiCurrency-Trade-Refactor P3 — 跨幣別 Mode 才暴露此欄位。
    /// </summary>
    [ObservableProperty] private string _fxRate = string.Empty;

    /// <summary>Validation error for <see cref="FxRate"/>.</summary>
    [ObservableProperty] private string _fxRateError = string.Empty;

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

    partial void OnInstrumentCurrencyChanged(string value)
    {
        OnPropertyChanged(nameof(IsCrossCurrency));
        OnPropertyChanged(nameof(InstrumentCurrencyBadge));
        OnPropertyChanged(nameof(SettlementPairDisplay));
    }

    partial void OnCashAccountCurrencyChanged(string value)
    {
        SettlementCurrency = string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();
        OnPropertyChanged(nameof(IsCrossCurrency));
        OnPropertyChanged(nameof(SettlementPairDisplay));
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

    /// <summary>
    /// 總計顯示文字。
    /// 總額模式 → 顯示使用者輸入的總額。
    /// 單價模式 → 顯示 數量 × 單價，由 caller 提供 price/qty。
    /// </summary>
    public string ComputeTotalDisplay(string addPriceText, string addQuantityText)
    {
        if (IsTotalMode &&
            ParseHelpers.TryParseDecimal(TotalCost, out var t) && t > 0)
            return t.ToString("N0");
        if (ParseHelpers.TryParseDecimal(addPriceText, out var p) && p > 0 &&
            ParseHelpers.TryParseInt(addQuantityText, out var q) && q > 0)
            return (p * q).ToString("N0");
        return "0";
    }

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
        SettlementInputMode = "statement";
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
