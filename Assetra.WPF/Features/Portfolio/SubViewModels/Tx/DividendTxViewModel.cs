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
    /// True when <see cref="InstrumentCurrency"/> ≠ <see cref="CashAccountCurrency"/>.
    /// Drives CashDividendTxForm's cross-currency banner + FxRate field visibility.
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

    partial void OnInstrumentCurrencyChanged(string value) => OnPropertyChanged(nameof(IsCrossCurrency));
    partial void OnCashAccountCurrencyChanged(string value) => OnPropertyChanged(nameof(IsCrossCurrency));

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
    }
}
