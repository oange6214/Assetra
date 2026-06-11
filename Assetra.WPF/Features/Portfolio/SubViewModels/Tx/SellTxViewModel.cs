using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio.SubViewModels.Tx;

/// <summary>
/// H1 — second child VM split off from <c>TransactionDialogViewModel</c>. Owns the
/// **sell transaction state cluster**:
/// <list type="bullet">
///   <item><see cref="Position"/>: which position to sell (PortfolioRowViewModel)</item>
///   <item><see cref="Quantity"/> + <see cref="QuantityError"/>: shares to sell</item>
///   <item>Preview values: <see cref="GrossAmount"/>, <see cref="Commission"/>,
///         <see cref="TransactionTax"/>, <see cref="NetAmount"/></item>
///   <item>Asset-class flags: <see cref="IsEtf"/>, <see cref="IsBondEtf"/> — drive the
///         Taiwan trade-fee calculator's tax rate</item>
/// </list>
///
/// <para>
/// The parent dialog VM reacts to <see cref="ObservableObject.PropertyChanged"/>
/// (Position / Quantity) to re-run <c>UpdateSellTxPreview</c>. Validation (
/// <see cref="QuantityError"/>) is also written by the parent since it depends on
/// the shared <c>ValidatePositiveIntOrEmpty</c> helper.
/// </para>
/// </summary>
public sealed partial class SellTxViewModel : ObservableObject
{
    [ObservableProperty] private PortfolioRowViewModel? _position;
    [ObservableProperty] private string _quantity = string.Empty;
    [ObservableProperty] private string _quantityError = string.Empty;

    // Preview — parallel to buy's AddGrossAmount / AddCommission / AddTotalCost,
    // but also shows TransactionTax + NetAmount since sell has two deductions.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private decimal _grossAmount;

    [ObservableProperty] private decimal _commission;
    [ObservableProperty] private decimal _transactionTax;
    [ObservableProperty] private decimal _netAmount;
    [ObservableProperty] private bool _isEtf;
    [ObservableProperty] private bool _isBondEtf;

    // MultiCurrency-Trade-Refactor P3 — Sell side mirrors Buy: when the
    // sold instrument's currency differs from the receiving cash account's
    // currency, expose ActualCashAmount + FxRate fields. Same field semantics
    // as BuyTxViewModel — see BuyTxViewModel for full docs.
    [ObservableProperty] private string _actualCashAmount = string.Empty;
    [ObservableProperty] private string _actualCashAmountError = string.Empty;
    [ObservableProperty] private string _fxRate = string.Empty;
    [ObservableProperty] private string _fxRateError = string.Empty;
    [ObservableProperty] private string _instrumentCurrency = string.Empty;
    [ObservableProperty] private string _cashAccountCurrency = string.Empty;

    /// <summary>
    /// P5.8a — Settlement input authority mirrors Buy. "statement" = ActualCashAmount
    /// is authoritative; "fx" = FxRate is authoritative and cash is estimated.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatementSettlementMode))]
    [NotifyPropertyChangedFor(nameof(IsFxSettlementMode))]
    private string _settlementInputMode = "statement";

    /// <summary>實際入帳的現金幣別，預設跟現金帳戶幣別一致。</summary>
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
    /// True when <see cref="InstrumentCurrency"/> ≠ <see cref="SettlementCurrency"/>.
    /// Empty currency on either side treated as "TWD" (avoids false positives during init).
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

    /// <summary>P3 — mirror of BuyTxViewModel.InstrumentCurrencyBadge for Sell side.</summary>
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

    public bool IsStatementSettlementMode => SettlementInputMode == "statement";
    public bool IsFxSettlementMode => SettlementInputMode == "fx";

    public bool HasPreview => GrossAmount > 0;

    /// <summary>Suppress auto-fill of TxAmount when Position is set programmatically (Edit mode).</summary>
    public bool SuppressPositionPriceAutoFill { get; set; }

    /// <summary>Reset preview values to 0 (called when sell is invalid / form opened fresh).</summary>
    public void ResetPreview()
    {
        GrossAmount = 0;
        Commission = 0;
        TransactionTax = 0;
        NetAmount = 0;
    }

    /// <summary>Reset all sell fields back to defaults (dialog open / type switch).</summary>
    public void Reset()
    {
        Position = null;
        Quantity = string.Empty;
        QuantityError = string.Empty;
        IsEtf = false;
        IsBondEtf = false;
        ActualCashAmount = string.Empty;
        ActualCashAmountError = string.Empty;
        FxRate = string.Empty;
        FxRateError = string.Empty;
        InstrumentCurrency = string.Empty;
        CashAccountCurrency = string.Empty;
        // P5.8a — reset Buy-mirror settlement metadata
        SettlementInputMode = "statement";
        SettlementCurrency = string.Empty;
        FxRateDate = null;
        FxSourceLabel = string.Empty;
        IsFxManual = false;
        FxFetchError = string.Empty;
        ResetPreview();
    }
}
