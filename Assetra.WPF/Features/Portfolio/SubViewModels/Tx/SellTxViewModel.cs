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
    /// True when <see cref="InstrumentCurrency"/> ≠ <see cref="CashAccountCurrency"/>.
    /// Empty currency on either side treated as "TWD" (avoids false positives during init).
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
        ResetPreview();
    }
}
