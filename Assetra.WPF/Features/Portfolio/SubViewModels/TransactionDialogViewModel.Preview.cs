using Assetra.Core.Models;
using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Impact preview — projects the post-save state of any account / liability
/// the dialog will touch, and exposes it as a single multi-line string for
/// the inline preview banner.
///
/// Coverage by trade type:
///
///   Cash account balance line
///   ─────────────────────────
///   * Income / CashDividend / Deposit / LoanBorrow → +amount
///   * Withdrawal / CreditCardPayment              → −amount
///   * LoanRepay                                    → −(principal + interest)
///   * Buy                                          → −actual cash debit, else preview total
///
///   Liability balance line
///   ──────────────────────
///   * LoanBorrow                                   → +amount  (Loan.Label)
///   * LoanRepay                                    → −principal (Loan.Label)
///   * CreditCardCharge                             → +amount  (CreditCard.Card)
///   * CreditCardPayment                            → −amount  (CreditCard.Card)
///
///   Skipped (no preview):
///   * Sell                  — meta-only edit; form hidden
///   * Transfer              — meta-only edit; form hidden
///   * StockDividend         — share count change rather than balance; deferred
///
/// Edit-mode correctness: in edit mode the displayed account / liability
/// Balance ALREADY includes the original trade's effect. The original delta
/// is backed out (per the same-target-vs-different-target rule) and the new
/// delta is then applied so the projection reflects post-save state.
///
/// Buy amount: the preview first uses the broker-confirmed actual cash debit
/// when present, then falls back to the buy preview total, then to gross cost.
/// </summary>
public partial class TransactionDialogViewModel
{
    /// <summary>True when the panel should render at least one preview line.</summary>
    public bool HasImpactPreview => !string.IsNullOrEmpty(ImpactPreviewText);

    /// <summary>
    /// Multi-line preview text. Each line covers one affected target
    /// (cash account or liability). Empty when no line applies.
    /// </summary>
    public string ImpactPreviewText
    {
        get
        {
            var lines = new List<string>(2);

            if (TryBuildCashLine() is { Length: > 0 } cashLine)
                lines.Add(cashLine);

            if (TryBuildLiabilityLine() is { Length: > 0 } liabLine)
                lines.Add(liabLine);

            return lines.Count == 0 ? string.Empty : string.Join('\n', lines);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Cash account line
    // ─────────────────────────────────────────────────────────────

    private string TryBuildCashLine()
    {
        if (TxCashAccount is not { } acct)
            return string.Empty;

        if (!TryComputeNewCashDelta(out var newDelta))
            return string.Empty;

        var originalDelta = ComputeOriginalCashDelta(acct);
        var projected = acct.Balance - originalDelta + newDelta;

        if (projected == acct.Balance)
            return string.Empty;

        return $"{acct.Name} {L("Portfolio.Tx.Preview.BalanceLabel", "餘額")}：" +
               $"{acct.Balance:N0} → {projected:N0}";
    }

    /// <summary>
    /// Computes the signed cash impact of the in-flight trade.
    /// </summary>
    private bool TryComputeNewCashDelta(out decimal delta)
    {
        delta = 0;
        switch (TxType)
        {
            case "income":
            case "deposit":
            case "loanBorrow":
                if (!ParseHelpers.TryParseDecimal(TxAmount, out var simpleAmount) || simpleAmount <= 0)
                    return false;
                delta = simpleAmount;
                return true;

            case "withdrawal":
            case "creditCardPayment":
                if (!ParseHelpers.TryParseDecimal(TxAmount, out var outAmount) || outAmount <= 0)
                    return false;
                delta = -outAmount;
                return true;

            case "cashDiv":
                if (Div.Total <= 0)
                    return false;
                delta = Div.Total;
                return true;

            case "loanRepay":
                ParseHelpers.TryParseDecimal(Loan.Principal, out var principal);
                ParseHelpers.TryParseDecimal(Loan.InterestPaid, out var interest);
                var totalCashOut = principal + interest;
                if (totalCashOut <= 0)
                    return false;
                delta = -totalCashOut;
                return true;

            case "buy":
                if (!TryReadBuyCashOut(out var cashOut) || cashOut <= 0)
                    return false;
                delta = -cashOut;
                return true;

            default:
                // Sell / Transfer / StockDiv / CreditCardCharge: no cash line.
                return false;
        }
    }

    private bool TryReadBuyCashOut(out decimal cashOut)
    {
        cashOut = 0;
        if (!string.IsNullOrWhiteSpace(Buy.ActualCashAmount))
            return ParseHelpers.TryParseDecimal(Buy.ActualCashAmount, out cashOut) && cashOut > 0;

        if (AddAssetDialog.AddTotalCost > 0)
        {
            cashOut = AddAssetDialog.AddTotalCost;
            return true;
        }

        return TryReadBuyGrossCost(out cashOut);
    }

    /// <summary>
    /// Reads gross Buy cost from the AddAssetDialog sub-VM, supporting both
    /// "unit price × quantity" and "total cost" input modes.
    /// </summary>
    private bool TryReadBuyGrossCost(out decimal gross)
    {
        gross = 0;
        if (Buy.PriceMode == "total")
        {
            return ParseHelpers.TryParseDecimal(Buy.TotalCost, out gross) && gross > 0;
        }

        if (!ParseHelpers.TryParseDecimal(AddAssetDialog.AddPrice, out var price) || price <= 0 ||
            !ParseHelpers.TryParseInt(AddAssetDialog.AddQuantity, out var qty) || qty <= 0)
            return false;

        gross = price * qty;
        return true;
    }

    private decimal ComputeOriginalCashDelta(CashAccountRowViewModel acct)
    {
        if (!IsEditMode || EditingTradeId is not { } id)
            return 0;

        var oldRow = Trades.FirstOrDefault(t => t.Id == id);
        if (oldRow is null)
            return 0;

        if (oldRow.CashAccountId is not { } accId || accId != acct.Id)
            return 0;

        var oldAmount = oldRow.CashAmount ?? 0m;
        return oldRow.Type switch
        {
            TradeType.Income
                or TradeType.CashDividend
                or TradeType.Deposit
                or TradeType.LoanBorrow => oldAmount,
            TradeType.Withdrawal
                or TradeType.CreditCardPayment
                or TradeType.LoanRepay => -oldAmount,
            TradeType.Buy => -BuyCashAmount(oldRow),
            TradeType.Sell => SellCashAmount(oldRow),
            _ => 0,
        };
    }

    private static decimal BuyCashAmount(TradeRowViewModel row) =>
        row.CashAmount ?? (row.Price * row.Quantity + (row.Commission ?? 0m));

    private static decimal SellCashAmount(TradeRowViewModel row) =>
        row.CashAmount ?? (row.Price * row.Quantity - (row.Commission ?? 0m));

    // ─────────────────────────────────────────────────────────────
    // Liability balance line
    // ─────────────────────────────────────────────────────────────

    private string TryBuildLiabilityLine()
    {
        if (!TryComputeNewLiabilityDelta(out var liability, out var newDelta))
            return string.Empty;

        var originalDelta = ComputeOriginalLiabilityDelta(liability);
        var projected = liability.Balance - originalDelta + newDelta;

        if (projected == liability.Balance)
            return string.Empty;

        return $"{liability.Name} {L("Portfolio.Tx.Preview.LiabilityLabel", "餘額")}：" +
               $"{liability.Balance:N0} → {projected:N0}";
    }

    /// <summary>
    /// Computes the signed liability-balance impact. The signed convention
    /// matches "remaining debt" — positive delta means the user owes more.
    /// </summary>
    private bool TryComputeNewLiabilityDelta(out LiabilityRowViewModel liability, out decimal delta)
    {
        liability = null!;
        delta = 0;

        switch (TxType)
        {
            case "loanBorrow":
                if (!ParseHelpers.TryParseDecimal(TxAmount, out var borrowed) || borrowed <= 0)
                    return false;
                if (FindLiabilityByLabel(Loan.Label) is not { } borrowLiab)
                    return false;
                liability = borrowLiab;
                delta = borrowed;
                return true;

            case "loanRepay":
                // Only the principal portion reduces remaining debt; interest
                // is a pure expense on the cash side.
                if (!ParseHelpers.TryParseDecimal(Loan.Principal, out var principalPaid) || principalPaid <= 0)
                    return false;
                if (FindLiabilityByLabel(Loan.Label) is not { } repayLiab)
                    return false;
                liability = repayLiab;
                delta = -principalPaid;
                return true;

            case "creditCardCharge":
                if (!ParseHelpers.TryParseDecimal(TxAmount, out var charged) || charged <= 0)
                    return false;
                if (CreditCard.Card is null)
                    return false;
                liability = CreditCard.Card;
                delta = charged;
                return true;

            case "creditCardPayment":
                if (!ParseHelpers.TryParseDecimal(TxAmount, out var paid) || paid <= 0)
                    return false;
                if (CreditCard.Card is null)
                    return false;
                liability = CreditCard.Card;
                delta = -paid;
                return true;

            default:
                return false;
        }
    }

    private LiabilityRowViewModel? FindLiabilityByLabel(string label) =>
        string.IsNullOrWhiteSpace(label)
            ? null
            : Liabilities.FirstOrDefault(l =>
                string.Equals(l.Label, label, StringComparison.OrdinalIgnoreCase));

    private decimal ComputeOriginalLiabilityDelta(LiabilityRowViewModel liability)
    {
        if (!IsEditMode || EditingTradeId is not { } id)
            return 0;

        var oldRow = Trades.FirstOrDefault(t => t.Id == id);
        if (oldRow is null)
            return 0;

        // Match by label/credit-card link. Loans use Symbol or Label string;
        // credit-card trades carry the issuer asset id via LiabilityAssetId.
        var sameTarget = oldRow.Type switch
        {
            TradeType.LoanBorrow or TradeType.LoanRepay =>
                string.Equals(oldRow.Symbol ?? oldRow.Name, liability.Label, StringComparison.OrdinalIgnoreCase),
            TradeType.CreditCardCharge or TradeType.CreditCardPayment =>
                liability.AssetId.HasValue && oldRow.LiabilityAssetId == liability.AssetId,
            _ => false,
        };
        if (!sameTarget)
            return 0;

        return oldRow.Type switch
        {
            TradeType.LoanBorrow => oldRow.CashAmount ?? 0m,
            TradeType.LoanRepay =>
                // Principal portion only — the row may not store split, so use the
                // best available figure (Principal field if available, else CashAmount).
                -(oldRow.Principal ?? oldRow.CashAmount ?? 0m),
            TradeType.CreditCardCharge => oldRow.CashAmount ?? 0m,
            TradeType.CreditCardPayment => -(oldRow.CashAmount ?? 0m),
            _ => 0,
        };
    }

    // ─────────────────────────────────────────────────────────────
    // Recompute hooks
    // ─────────────────────────────────────────────────────────────

    private void NotifyImpactPreviewChanged()
    {
        OnPropertyChanged(nameof(ImpactPreviewText));
        OnPropertyChanged(nameof(HasImpactPreview));
    }

    partial void OnTxCashAccountChanged(CashAccountRowViewModel? value)
    {
        NotifyImpactPreviewChanged();
        // P3 — push selected cash account's currency into Buy / Sell / Div VMs so XAML can
        // toggle the FX-rate field via *.IsCrossCurrency.
        var ccy = value?.Currency ?? string.Empty;
        Buy.CashAccountCurrency = ccy;
        Buy.SettlementCurrency = ccy;
        Sell.CashAccountCurrency = ccy;
        Div.CashAccountCurrency = ccy;
        FetchBuyFxRateCommand.NotifyCanExecuteChanged();
        QueueBuyFxRateRefresh();
    }

    // OnTxCreditCardChanged retired — CreditCard sub-VM PropertyChanged is wired
    // via OnCreditCardTxChanged in the main partial.
}
