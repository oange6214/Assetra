using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Models;
using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Confirm.cs split — Credit-card Charge + Payment confirmation.
/// </summary>
public partial class TransactionDialogViewModel
{
    private async Task ConfirmCreditCardChargeAsync()
    {
        if (CreditCard.Card?.AssetId is not { } cardId)
        { TxError = "請選擇信用卡"; return; }
        if (!ParseHelpers.TryParseDecimal(TxAmount, out var amount) || amount <= 0)
        { TxError = "金額無效"; return; }

        var tradeDate = DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime();
        await _creditCardTransactionWorkflowService.ChargeAsync(new CreditCardChargeRequest(
            cardId,
            CreditCard.Card.Label,
            tradeDate,
            amount,
            string.IsNullOrWhiteSpace(TxNote) ? null : TxNote,
            TxCategoryId));

        await AfterTxSuccessAsync();
    }

    private async Task ConfirmCreditCardPaymentAsync()
    {
        if (CreditCard.Card?.AssetId is not { } cardId)
        { TxError = "請選擇信用卡"; return; }
        if (!ParseHelpers.TryParseDecimal(TxAmount, out var amount) || amount <= 0)
        { TxError = "金額無效"; return; }
        if (CreditCard.Card.Balance <= 0)
        {
            TxError = L("Portfolio.Tx.CreditCardPayment.NoBalance",
                "這張信用卡目前沒有未繳金額。若是補登過去帳單，請先新增「信用卡消費」，或改用「提款」。");
            return;
        }
        if (amount > CreditCard.Card.Balance)
        {
            TxError = L("Portfolio.Tx.CreditCardPayment.ExceedsBalance",
                "繳款金額不可超過目前未繳金額。若是補登過去帳單，請先新增「信用卡消費」，或改用「提款」。");
            return;
        }
        var cashAccId = await ResolveCashAccountIdAsync();
        if (cashAccId is null)
        {
            TxError = CashAccounts.Count == 0
                ? "尚無帳戶，請先建立帳戶"
                : "請選擇扣款帳戶";
            return;
        }

        var accountName = TxCashAccount?.Name ?? TxCashAccountName.Trim();
        var tradeDate = DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime();
        await _creditCardTransactionWorkflowService.PayAsync(new CreditCardPaymentRequest(
            cardId,
            CreditCard.Card.Label,
            cashAccId.Value,
            accountName,
            tradeDate,
            amount,
            string.IsNullOrWhiteSpace(TxNote) ? null : TxNote));

        await AfterTxSuccessAsync();
    }
}
