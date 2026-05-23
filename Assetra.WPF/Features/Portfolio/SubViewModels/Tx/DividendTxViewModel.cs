using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio.SubViewModels.Tx;

/// <summary>
/// H1 — third child VM split off from <c>TransactionDialogViewModel</c>. Owns the
/// **dividend transaction state cluster** for both cash- and stock-dividend types:
/// <list type="bullet">
///   <item>Cash dividend: <see cref="Position"/>, <see cref="PerShare"/>,
///         <see cref="Total"/>, <see cref="InputMode"/>, <see cref="TotalInput"/>
///         + per-share / total-input validation errors</item>
///   <item>Stock dividend: <see cref="StockPosition"/>, <see cref="StockNewShares"/>
///         + new-shares validation error</item>
/// </list>
///
/// <para>
/// Parent dialog VM listens to <see cref="ObservableObject.PropertyChanged"/> for
/// validation + total recomputation side effects (parallel to the Buy/Sell pattern).
/// </para>
/// </summary>
public sealed partial class DividendTxViewModel : ObservableObject
{
    // ── Cash dividend ───────────────────────────────────────────────────

    [ObservableProperty] private PortfolioRowViewModel? _position;
    [ObservableProperty] private string _perShare = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    private decimal _total;

    [ObservableProperty] private string _perShareError = string.Empty;

    /// <summary>"perShare" = 填每股股利；"total" = 直接填總股息金額。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPerShareMode))]
    [NotifyPropertyChangedFor(nameof(IsTotalMode))]
    private string _inputMode = "perShare";

    [ObservableProperty] private string _totalInput = string.Empty;
    [ObservableProperty] private string _totalInputError = string.Empty;

    public bool IsPerShareMode => InputMode == "perShare";
    public bool IsTotalMode => InputMode == "total";
    public bool HasPreview => Total > 0;

    // ── Stock dividend ──────────────────────────────────────────────────

    [ObservableProperty] private PortfolioRowViewModel? _stockPosition;
    [ObservableProperty] private string _stockNewShares = string.Empty;
    [ObservableProperty] private string _stockNewSharesError = string.Empty;

    // ── Cross-currency (cash dividend only; stock dividend has no cash flow) ──
    // MultiCurrency-Trade-Refactor P3 — same shape as BuyTxViewModel / SellTxViewModel.

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettlementCurrencyDisplay))]
    [NotifyPropertyChangedFor(nameof(SettlementPairDisplay))]
    private string _settlementCurrency = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FxRateDateDisplay))]
    private DateOnly? _fxRateDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FxSourceDisplay))]
    private string _fxSourceLabel = string.Empty;

    [ObservableProperty] private bool _isFxManual;
    [ObservableProperty] private string _fxFetchError = string.Empty;

    /// <summary>
    /// True when <see cref="InstrumentCurrency"/> ≠ <see cref="SettlementCurrency"/>.
    /// Drives CashDividendTxForm's cross-currency banner + FxRate field visibility.
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

    /// <summary>Reset all dividend fields back to defaults.</summary>
    public void Reset()
    {
        Position = null;
        PerShare = string.Empty;
        Total = 0m;
        PerShareError = string.Empty;
        InputMode = "perShare";
        TotalInput = string.Empty;
        TotalInputError = string.Empty;
        StockPosition = null;
        StockNewShares = string.Empty;
        StockNewSharesError = string.Empty;
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
    }
}
