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
            var cash = string.IsNullOrWhiteSpace(CashAccountCurrency) ? "TWD" : CashAccountCurrency.Trim().ToUpperInvariant();
            return !string.Equals(instr, cash, StringComparison.OrdinalIgnoreCase);
        }
    }

    partial void OnInstrumentCurrencyChanged(string value)
    {
        OnPropertyChanged(nameof(IsCrossCurrency));
        OnPropertyChanged(nameof(InstrumentCurrencyBadge));
    }
    partial void OnCashAccountCurrencyChanged(string value) => OnPropertyChanged(nameof(IsCrossCurrency));

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

    public bool IsStock => AssetType == "stock";
    public bool IsNonStock => AssetType is "fund" or "metal" or "bond";
    public bool IsCrypto => AssetType == "crypto";

    public bool IsUnitMode => PriceMode == "unit";
    public bool IsTotalMode => PriceMode == "total";

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
        // P3
        FxRate = string.Empty;
        FxRateError = string.Empty;
        InstrumentCurrency = string.Empty;
        CashAccountCurrency = string.Empty;
    }
}
