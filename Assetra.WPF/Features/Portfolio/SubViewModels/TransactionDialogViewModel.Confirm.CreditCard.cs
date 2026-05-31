using Assetra.Application.Portfolio.Dtos;
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

        // 餘額守門檢查只對「新增繳款」生效。編輯既有繳款時，當前 Card.Balance 已經把
        // 「自己這筆」當作扣除進去了——所以一張剛剛繳清的卡 Balance 必然 = 0，這時跑
        // 「目前沒有未繳金額」會在使用者只想改個日期 / 備註時誤判，把儲存擋掉。
        // 同理 amount > Balance 的檢查也只在新增時有意義（編輯時舊金額已經算進 Balance）。
        //
        // ⚠ 用 IsRevisionMode 而非 IsEditMode 判斷：ConfirmTx() 在分派到本方法「之前」
        // 就已經把 EditingTradeId 清成 null（見 Confirm.cs 第 79 行），並把編輯升級成
        // 隱性修訂（IsRevisionMode = true）。所以執行到這裡時 IsEditMode 永遠是 false，
        // 真正還活著的「這是編輯／修訂、不是全新繳款」訊號是 IsRevisionMode。
        if (!IsRevisionMode)
        {
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
