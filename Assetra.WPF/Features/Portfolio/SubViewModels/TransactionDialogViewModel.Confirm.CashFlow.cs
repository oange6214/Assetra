using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Models;
using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Confirm.cs split — Income + Deposit/Withdrawal (cash-flow) confirmation.
/// </summary>
public partial class TransactionDialogViewModel
{
    private async Task ConfirmIncomeAsync()
    {
        if (!ParseHelpers.TryParseDecimal(TxAmount, out var amount) || amount <= 0)
        { TxError = "金額無效"; return; }
        var fee = ParseOptionalFee(out var feeError);
        if (feeError is not null)
        { TxError = feeError; return; }

        // 「計入現金餘額」勾選時才連動現金帳戶；取消勾選＝不連動（只記錄收入、不影響任何餘額）。
        var cashAccId = TxUseCashAccount ? await ResolveCashAccountIdAsync() : null;
        // AccountName drives the trade-list 資產 column. Prefer the resolved
        // ResolveCashAccountIdAsync row (covers the typed-but-not-yet-persisted
        // case where TxCashAccount is null but TxCashAccountName is set);
        // fall back to TxCashAccount.Name; otherwise empty.
        var accountName = cashAccId is { } id
            ? CashAccounts.FirstOrDefault(c => c.Id == id)?.Name ?? string.Empty
            : TxCashAccount?.Name ?? string.Empty;
        var tradeDate = DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime();
        await _transactionWorkflowService.RecordIncomeAsync(new IncomeTransactionRequest(
            amount,
            tradeDate,
            cashAccId,
            accountName,
            TxNote,
            fee,
            TxCategoryId));

        await AfterTxSuccessAsync();
    }

    /// <summary>存入/提款 → 現金帳戶餘額增減。可選填手續費（跨行匯款費、ATM 費等）。</summary>
    private async Task ConfirmCashFlowAsync(TradeType type)
    {
        if (!ParseHelpers.TryParseDecimal(TxAmount, out var amount) || amount <= 0)
        { TxError = "金額無效"; return; }
        var fee = ParseOptionalFee(out var feeError);
        if (feeError is not null)
        { TxError = feeError; return; }

        var cashAccId = await ResolveCashAccountIdAsync();
        if (cashAccId is null)
        {
            TxError = CashAccounts.Count == 0
                ? "尚無帳戶，請先建立帳戶"
                : "請選擇帳戶";
            return;
        }
        var accountName = TxCashAccount?.Name ?? TxCashAccountName.Trim();

        var tradeDate = DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime();
        var cleanNote = string.IsNullOrWhiteSpace(TxNote) ? null : TxNote;
        await _transactionWorkflowService.RecordCashFlowAsync(new CashFlowTransactionRequest(
            type,
            amount,
            tradeDate,
            cashAccId.Value,
            accountName,
            cleanNote,
            fee,
            TxCategoryId));

        await AfterTxSuccessAsync();
    }
}
