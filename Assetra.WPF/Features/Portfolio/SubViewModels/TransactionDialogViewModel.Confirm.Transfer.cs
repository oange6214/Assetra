using Assetra.Application.Portfolio.Dtos;
using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Confirm.cs split — Transfer transaction confirmation.
/// </summary>
public partial class TransactionDialogViewModel
{
    /// <summary>轉帳 → 在兩個現金帳戶之間搬錢。</summary>
    private async Task ConfirmTransferAsync()
    {
        if (!ParseHelpers.TryParseDecimal(TxAmount, out var srcAmount) || srcAmount <= 0)
        { TxError = "轉出金額無效"; return; }
        if (!ParseHelpers.TryParseDecimal(Transfer.TargetAmount, out var dstAmount) || dstAmount <= 0)
        { TxError = "轉入金額無效"; return; }
        if (CashAccounts.Count < 2)
        { TxError = "至少需要兩個帳戶才能轉帳"; return; }
        if (TxCashAccount is null)
        { TxError = "請選擇來源帳戶"; return; }
        var destId = await ResolveTransferTargetIdAsync();
        if (destId is null)
        { TxError = "請選擇轉入帳戶"; return; }
        if (TxCashAccount.Id == destId)
        { TxError = "來源與目標不能是同一個帳戶"; return; }
        var fee = ParseOptionalFee(out var feeError);
        if (feeError is not null)
        { TxError = feeError; return; }

        var tradeDate = DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime();
        var srcName = TxCashAccount.Name;
        var dstName = Transfer.Target?.Name ?? Transfer.TargetName.Trim();
        var userNote = string.IsNullOrWhiteSpace(TxNote) ? null : TxNote;

        await _transactionWorkflowService.RecordTransferAsync(new TransferTransactionRequest(
            TxCashAccount.Id,
            srcName,
            destId.Value,
            dstName,
            srcAmount,
            dstAmount,
            tradeDate,
            userNote,
            fee));

        await AfterTxSuccessAsync();
        // TransactionCompleted raised by ConfirmTx
    }
}
