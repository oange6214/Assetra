using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Models;
using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Confirm.cs split — LoanBorrow / LoanRepay confirmation +
/// AutoFillLoanRepayAsync helper (next-unpaid-entry pre-fill).
/// </summary>
public partial class TransactionDialogViewModel
{
    /// <summary>借款/還款 → 負債帳戶與現金帳戶同步調整；可選填手續費。</summary>
    private async Task ConfirmLoanAsync(TradeType type)
    {
        // ── 金額解析（LoanBorrow vs LoanRepay 走不同欄位）────────────────
        decimal cashAmount;
        decimal? principal = null;
        decimal? interestPaid = null;

        if (type == TradeType.LoanBorrow)
        {
            if (!ParseHelpers.TryParseDecimal(TxAmount, out var amt) || amt <= 0)
            { TxError = "金額無效"; return; }
            cashAmount = amt;
        }
        else // LoanRepay
        {
            if (!ParseHelpers.TryParseDecimal(Loan.Principal, out var p) || p < 0)
            { TxError = "本金無效"; return; }
            var rawInterest = string.IsNullOrWhiteSpace(Loan.InterestPaid) ? "0" : Loan.InterestPaid;
            if (!ParseHelpers.TryParseDecimal(rawInterest, out var ip) || ip < 0)
            { TxError = "利息金額無效"; return; }
            if (p + ip <= 0)
            { TxError = "本金與利息合計不得為零"; return; }
            principal = p;
            interestPaid = ip > 0 ? ip : null;
            cashAmount = p + ip;
        }

        if (string.IsNullOrWhiteSpace(Loan.Label))
        { TxError = "請輸入或選擇貸款名稱"; return; }

        // ── 攤還表欄位（選填；借款時才適用）────────────────────────────────
        decimal? amortAnnualRate = null;
        int? amortTermMonths = null;
        if (type == TradeType.LoanBorrow &&
            !string.IsNullOrWhiteSpace(Loan.Rate) &&
            !string.IsNullOrWhiteSpace(Loan.TermMonths))
        {
            if (!ParseHelpers.TryParseDecimal(Loan.Rate, out var ratePct) || ratePct < 0)
            { TxError = "年利率無效"; return; }
            if (!ParseHelpers.TryParseInt(Loan.TermMonths, out var termMo) || termMo <= 0)
            { TxError = "還款期數無效"; return; }
            amortAnnualRate = ratePct / 100m;
            amortTermMonths = termMo;
        }

        var fee = ParseOptionalFee(out var feeError);
        if (feeError is not null)
        { TxError = feeError; return; }

        var tradeDate = DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime();
        var liabName = Loan.Label.Trim();
        var cleanNote = string.IsNullOrWhiteSpace(TxNote) ? null : TxNote;
        var cashAccId = TxUseCashAccount ? await ResolveCashAccountIdAsync() : null;

        await _loanMutationWorkflowService.RecordAsync(new LoanTransactionRequest(
            type,
            cashAmount,
            tradeDate,
            liabName,
            cashAccId,
            cleanNote,
            fee,
            principal,
            interestPaid,
            amortAnnualRate,
            amortTermMonths,
            amortAnnualRate.HasValue && amortTermMonths.HasValue ? DateOnly.FromDateTime(Loan.StartDate) : null));

        await AfterTxSuccessAsync(
            reloadLiabilities: amortAnnualRate.HasValue,
            rebuildTotals: amortAnnualRate.HasValue);
        // TransactionCompleted raised by ConfirmTx
    }

    /// <summary>Pre-fills <see cref="Loan.Principal"/> + <see cref="Loan.InterestPaid"/>
    /// from the next unpaid amortization entry of the selected loan label.</summary>
    private async Task AutoFillLoanRepayAsync(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return;

        var row = Liabilities.FirstOrDefault(r =>
            string.Equals(r.Label, label.Trim(), StringComparison.OrdinalIgnoreCase));
        if (row is null || !row.IsLoan)
            return;

        if (!row.IsScheduleLoaded)
            await _loadLoanScheduleAsync(row);

        var next = row.NextUnpaidEntry;
        if (next is null)
            return;

        Loan.Principal = next.PrincipalAmount.ToString("F0");
        Loan.InterestPaid = next.InterestAmount > 0 ? next.InterestAmount.ToString("F0") : string.Empty;
    }
}
