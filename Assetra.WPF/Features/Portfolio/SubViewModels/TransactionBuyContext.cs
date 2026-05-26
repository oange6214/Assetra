namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Production adapter that exposes a snapshot of <see cref="TransactionDialogViewModel"/>
/// state via the <see cref="IBuyExecutionContext"/> interface. Each property
/// reads through to the live VM at call-time, so the buy flow always sees the
/// latest values the user has typed (no caching).
///
/// Wired once in <c>PortfolioViewModel</c> by:
/// <code>
/// AddAssetDialog.BuyContext = new TransactionBuyContext(Transaction);
/// </code>
/// </summary>
public sealed class TransactionBuyContext(TransactionDialogViewModel vm) : IBuyExecutionContext
{
    public decimal CommissionDiscount => vm.TxCommissionDiscountValue;
    public string TxFee => vm.TxFee;
    public string ActualCashAmount => vm.Buy.ActualCashAmount;
    public string SettlementInputMode => vm.Buy.SettlementInputMode;
    public bool BuyMetaOnly => vm.Buy.MetaOnly;
    public Guid? CashAccountId => vm.TxCashAccount?.Id;
    public string? CashAccountCurrency => vm.TxCashAccount?.Currency;
    public bool UseCashAccount => vm.TxUseCashAccount;
    public bool BuyIsTotalMode => vm.Buy.IsTotalMode;
    public bool BuyTotalIncludesFee => vm.Buy.TotalIncludesFee;
    public string FxRate => vm.Buy.FxRate;
    // Expose the selected asset/symbol currency. This is intentionally separate
    // from TxCurrency and the funding account currency.
    public string InstrumentCurrency => vm.Buy.InstrumentCurrency;
    public string SettlementCurrency => vm.Buy.SettlementCurrency;
    public DateOnly? FxRateDate => vm.Buy.FxRateDate;
    public string? FxSource => vm.Buy.FxSourceLabel;
    public bool IsFxManual => vm.Buy.IsFxManual;
}
