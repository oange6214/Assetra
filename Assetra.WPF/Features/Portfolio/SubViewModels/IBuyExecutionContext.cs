namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Snapshot of the transaction-dialog state that the Buy / asset-creation flow
/// in <see cref="AddAssetDialogViewModel"/> needs to read at confirm-time.
/// Replaces the previous service-locator pattern of 7 individual
/// <c>Func&lt;…&gt;</c> properties (<c>GetTxFee</c>, <c>GetTxBuyMetaOnly</c>,
/// <c>GetTxCashAccountId</c>, …) with a single typed interface.
///
/// <para>
/// Each implementation is a thin read-only adapter over its real source —
/// production wiring is <c>TransactionBuyContext</c> (delegates to
/// <c>TransactionDialogViewModel</c>); tests use <see cref="StaticBuyContext"/>
/// for fixed values. Defaults on this interface mirror the Func defaults so
/// any consumer that doesn't set <c>BuyContext</c> still gets safe behaviour.
/// </para>
/// </summary>
public interface IBuyExecutionContext
{
    /// <summary>Commission discount multiplier in 0–1 (1.0 = no discount). Default 1m.</summary>
    decimal CommissionDiscount { get; }

    /// <summary>Raw text of the optional manual fee field. Empty string when not used.</summary>
    string TxFee { get; }

    /// <summary>
    /// Raw text of the optional actual cash debit field. This is the amount
    /// deducted from the linked cash account, in that account's currency.
    /// Empty string means "estimate from the trade price and fee".
    /// </summary>
    string ActualCashAmount { get; }

    /// <summary>
    /// Cross-currency settlement input authority. "statement" means
    /// <see cref="ActualCashAmount"/> is authoritative; "fx" means
    /// <see cref="FxRate"/> is authoritative and cash amount is estimated.
    /// </summary>
    string SettlementInputMode { get => "statement"; }

    /// <summary>True when the buy is "metadata only" (no Trade record written).</summary>
    bool BuyMetaOnly { get; }

    /// <summary>Cash account id to debit; null = no cash linkage.</summary>
    Guid? CashAccountId { get; }

    /// <summary>Currency of the selected cash account, when known.</summary>
    string? CashAccountCurrency { get; }

    /// <summary>True when the buy should debit a cash account.</summary>
    bool UseCashAccount { get; }

    /// <summary>True when the dialog is in "total cost" buy-price mode (vs "unit price").</summary>
    bool BuyIsTotalMode { get; }

    /// <summary>
    /// True when the user marked the total-cost field as already including
    /// commission. Combined with <see cref="BuyIsTotalMode"/> drives the
    /// "force commission = 0" path so Trade.CashAmount matches the user input.
    /// </summary>
    bool BuyTotalIncludesFee { get; }

    /// <summary>
    /// Raw text of the optional FX rate field (instrument currency → funding currency).
    /// Empty string = same currency or user hasn't filled. Parsed at confirm-time.
    /// MultiCurrency-Trade-Refactor P3.
    /// </summary>
    string FxRate { get => string.Empty; }

    /// <summary>
    /// Effective instrument (input) currency. For investment trades this comes
    /// from the selected asset/symbol and remains separate from the cash-account
    /// debit currency used by settlement.
    /// </summary>
    string InstrumentCurrency { get => string.Empty; }

    /// <summary>Currency of the cash settlement movement. Defaults to cash-account currency.</summary>
    string SettlementCurrency { get => string.Empty; }

    /// <summary>Effective historical FX rate date, when the rate was fetched or chosen.</summary>
    DateOnly? FxRateDate { get => null; }

    /// <summary>FX source/audit label, for example Bank of Taiwan. Null for same-currency/manual-empty trades.</summary>
    string? FxSource { get => null; }

    /// <summary>True when the FX rate came from a user override instead of the resolver.</summary>
    bool IsFxManual { get => false; }
}

/// <summary>
/// Default-only implementation. Used when <c>AddAssetDialog.BuyContext</c>
/// is not wired (e.g. in test fixtures that don't exercise the buy path).
/// </summary>
public sealed class NullBuyContext : IBuyExecutionContext
{
    public static readonly NullBuyContext Instance = new();

    public decimal CommissionDiscount => 1m;
    public string TxFee => string.Empty;
    public string ActualCashAmount => string.Empty;
    public string SettlementInputMode => "statement";
    public bool BuyMetaOnly => false;
    public Guid? CashAccountId => null;
    public string? CashAccountCurrency => null;
    public bool UseCashAccount => false;
    public bool BuyIsTotalMode => false;
    public bool BuyTotalIncludesFee => true;
}

/// <summary>
/// Test helper providing fixed values via constructor; lets unit tests build
/// a context without constructing a full <c>TransactionDialogViewModel</c>.
/// </summary>
public sealed class StaticBuyContext(
    decimal commissionDiscount = 1m,
    string txFee = "",
    string actualCashAmount = "",
    string settlementInputMode = "statement",
    bool buyMetaOnly = false,
    Guid? cashAccountId = null,
    string? cashAccountCurrency = null,
    bool useCashAccount = false,
    bool buyIsTotalMode = false,
    bool buyTotalIncludesFee = true,
    string fxRate = "",
    string instrumentCurrency = "",
    string settlementCurrency = "",
    DateOnly? fxRateDate = null,
    string? fxSource = null,
    bool isFxManual = false)
    : IBuyExecutionContext
{
    public decimal CommissionDiscount { get; } = commissionDiscount;
    public string TxFee { get; } = txFee;
    public string ActualCashAmount { get; } = actualCashAmount;
    public string SettlementInputMode { get; } = settlementInputMode;
    public bool BuyMetaOnly { get; } = buyMetaOnly;
    public Guid? CashAccountId { get; } = cashAccountId;
    public string? CashAccountCurrency { get; } = cashAccountCurrency;
    public bool UseCashAccount { get; } = useCashAccount;
    public bool BuyIsTotalMode { get; } = buyIsTotalMode;
    public bool BuyTotalIncludesFee { get; } = buyTotalIncludesFee;
    public string FxRate { get; } = fxRate;
    public string InstrumentCurrency { get; } = instrumentCurrency;
    public string SettlementCurrency { get; } = settlementCurrency;
    public DateOnly? FxRateDate { get; } = fxRateDate;
    public string? FxSource { get; } = fxSource;
    public bool IsFxManual { get; } = isFxManual;
}
