using Assetra.Core.Models;
using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Impact preview — computes the post-transaction cash-account balance based
/// on the dialog's current state and exposes it as bindable strings for the
/// inline preview panel.
///
/// MVP scope (Phase 1): only preview the SELECTED cash account's balance for
/// trade types whose primary effect is a cash inflow/outflow:
///   * Income / CashDividend / Deposit / LoanBorrow → +amount
///   * Withdrawal / CreditCardPayment / LoanRepay   → −amount
///
/// Out of scope for Phase 1 (return false from <see cref="HasImpactPreview"/>):
///   * Buy / Sell — fees and commission discount make the math noisier; deferred
///   * Transfer  — meta-only edit, panel hidden anyway
///   * StockDividend / CreditCardCharge — no cash impact
///
/// Edit-mode correctness: in edit mode the displayed account.Balance ALREADY
/// includes the original trade's effect. We back out the original delta and
/// apply the new one so the preview reflects what the balance will be AFTER
/// save.
/// </summary>
public partial class TransactionDialogViewModel
{
    /// <summary>True when there is enough info to render the preview line.</summary>
    public bool HasImpactPreview => !string.IsNullOrEmpty(ImpactPreviewText);

    /// <summary>
    /// Single-line preview text. Empty when the panel should hide.
    /// Format: "現金帳戶 OOO 餘額：NT$50,000 → NT$48,000"
    /// (or just "→ NT$48,000" when the new value equals current — i.e. nothing
    /// to show — the property returns empty in that case).
    /// </summary>
    public string ImpactPreviewText
    {
        get
        {
            if (TxCashAccount is not { } acct)
                return string.Empty;

            if (!TryComputeNewSignedDelta(out var newDelta))
                return string.Empty;

            var originalDelta = ComputeOriginalDeltaForCurrentAccount(acct);
            var projected = acct.Balance - originalDelta + newDelta;

            // Skip when the projected balance is identical to current (e.g. user
            // edited only date/note while leaving amount unchanged). Avoids
            // showing a "X → X" line that adds no information.
            if (projected == acct.Balance)
                return string.Empty;

            return $"{acct.Name} {L("Portfolio.Tx.Preview.BalanceLabel", "餘額")}：" +
                   $"{acct.Balance:N0} → {projected:N0}";
        }
    }

    /// <summary>
    /// Computes the signed cash impact of the trade if it were saved right now.
    /// Returns false when the type isn't supported by Phase 1 or the amount
    /// can't be parsed.
    /// </summary>
    private bool TryComputeNewSignedDelta(out decimal delta)
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
                // TxDivTotal is computed by the dividend form (per-share × position
                // shares OR total-input mode). When zero/negative, no preview.
                if (TxDivTotal <= 0)
                    return false;
                delta = TxDivTotal;
                return true;

            case "loanRepay":
                // Both fields are strings; either may be empty (e.g. interest-free
                // micro-loans). Sum what's there.
                ParseHelpers.TryParseDecimal(TxPrincipal, out var principal);
                ParseHelpers.TryParseDecimal(TxInterestPaid, out var interest);
                var totalCashOut = principal + interest;
                if (totalCashOut <= 0)
                    return false;
                delta = -totalCashOut;
                return true;

            default:
                // Buy/Sell/Transfer/StockDiv/CreditCardCharge — out of MVP scope.
                return false;
        }
    }

    /// <summary>
    /// In edit mode, derive how much the original trade contributed to the
    /// CURRENTLY-selected cash account. If the account changed during this
    /// edit (oldRow used a different account), the contribution to this
    /// account is zero — it never received the original delta.
    /// In new-trade mode this is always zero.
    /// </summary>
    private decimal ComputeOriginalDeltaForCurrentAccount(CashAccountRowViewModel acct)
    {
        if (!IsEditMode || EditingTradeId is not { } id)
            return 0;

        var oldRow = Trades.FirstOrDefault(t => t.Id == id);
        if (oldRow is null)
            return 0;

        // Only same-account edits affect the displayed Balance baseline.
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
            _ => 0,
        };
    }

    /// <summary>
    /// Forces the inline preview to recompute. Called from existing partial
    /// On*Changed hooks for fields that feed into the delta calculation.
    /// Cheaper than wiring a dedicated event since the preview is just a
    /// computed string.
    /// </summary>
    private void NotifyImpactPreviewChanged()
    {
        OnPropertyChanged(nameof(ImpactPreviewText));
        OnPropertyChanged(nameof(HasImpactPreview));
    }

    /// <summary>
    /// Hook for the [ObservableProperty] generated TxCashAccount setter so
    /// switching account in the dropdown immediately re-projects the preview
    /// against the newly-selected account's Balance.
    /// </summary>
    partial void OnTxCashAccountChanged(CashAccountRowViewModel? value) =>
        NotifyImpactPreviewChanged();
}
