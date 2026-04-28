using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Models;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// TransactionDialogViewModel partial — Confirm command dispatch and per-trade-type
/// Confirm*Async submission helpers. Also hosts the meta-only update path for edits,
/// the loan-repay auto-fill, fee parsing, and the trade-deletion DTO mapping helper.
/// </summary>
public partial class TransactionDialogViewModel
{
    [RelayCommand]
    private async Task ConfirmTx()
    {
        TxError = string.Empty;

        // Snapshot the edit target but don't touch it yet. Each Confirm*Async does its own
        // validation and sets TxError on failure; if the new values are rejected we need the
        // old trade intact so the dialog can stay open with the edit context preserved.
        // This replaces the former "delete old before dispatch" pattern which could leave
        // the ledger in a corrupt state when validation failed (old gone, new never created).
        var pendingEditId = EditingTradeId;
        var oldRow = pendingEditId is { } id ? Trades.FirstOrDefault(t => t.Id == id) : null;

        // Clear EditingTradeId so downstream handlers (e.g. inline "which symbol is this
        // buy for" logic) don't mistake this for an in-progress edit loop.
        EditingTradeId = null;

        // Edit mode is intentionally limited to metadata-only updates. Core trade fields are
        // shown as a read-only summary in the dialog; users should create a revision instead
        // of mutating the economic shape of an existing record in place.
        if (oldRow is not null)
        {
            await UpdateTradeMetaOnlyAsync(oldRow);
            return;
        }

        var revisionSourceTradeId = _revisionSourceTradeId;
        _preserveRevisionSourceOnClose = revisionSourceTradeId.HasValue;

        switch (TxType)
        {
            case "income":
                await ConfirmIncomeAsync();
                break;
            case "cashDiv":
                await ConfirmCashDivAsync();
                break;
            case "stockDiv":
                await ConfirmStockDivAsync();
                break;
            case "deposit":
                await ConfirmCashFlowAsync(TradeType.Deposit);
                break;
            case "withdrawal":
                await ConfirmCashFlowAsync(TradeType.Withdrawal);
                break;
            case "loanBorrow":
                await ConfirmLoanAsync(TradeType.LoanBorrow);
                break;
            case "loanRepay":
                await ConfirmLoanAsync(TradeType.LoanRepay);
                break;
            case "creditCardCharge":
                await ConfirmCreditCardChargeAsync();
                break;
            case "creditCardPayment":
                await ConfirmCreditCardPaymentAsync();
                break;
            case "transfer":
                await ConfirmTransferAsync();
                break;
            case "buy":
                await ConfirmBuyAsync();
                break;
            case "sell":
                await ConfirmSellTxAsync();
                break;
        }

        _preserveRevisionSourceOnClose = false;

        // Validation / creation failed → restore edit mode and leave the old trade alone.
        if (!string.IsNullOrEmpty(TxError))
        {
            EditingTradeId = pendingEditId;
            return;
        }

        if (revisionSourceTradeId.HasValue)
        {
            IsRevisionMode = false;
            IsRevisionReplacePromptOpen = true;
            RevisionReplacePromptError = string.Empty;
            IsTxDialogOpen = true;
            return;
        }

        // New trade is in the ledger. Under the single-truth model, balances are a pure
        // projection over remaining journal entries — just delete the old row and re-project.
        // Note: oldRow is a snapshot taken before LoadTradesAsync (called inside the
        // Confirm*Async), so it's detached from the current Trades collection but its
        // data fields are still valid for the stock-lot cleanup below.
        if (oldRow is not null)
        {
            try
            {
                var result = await _tradeDeletionWorkflowService.DeleteAsync(ToTradeDeletionRequest(oldRow));
                if (!result.Success && result.BlockedBySell)
                {
                    TxError = L("Portfolio.Trade.DeleteBlockedBySell",
                        "請先刪除此股票的賣出記錄，再修改此買入記錄。");
                    EditingTradeId = pendingEditId;
                    return;
                }
            }
            catch (Exception ex)
            {
                // The new trade was already written; failing to delete the old one would
                // leave both records in the ledger and show a duplicate entry to the user.
                Log.Error(ex, "Failed to remove old trade {TradeId} during edit — possible duplicate entry", oldRow.Id);
                _snackbar?.Error(L("Portfolio.Trade.OldDeleteFailed", "舊交易記錄刪除失敗，資料庫可能存在重複筆數，建議重新整理或重啟應用程式"));
            }
            await _loadPositionsAsync();
            await _loadTradesAsync();
            await _reloadAccountBalancesAsync();
            _rebuildTotals();
        }

        TransactionCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Thin wrapper delegating to <see cref="TradeRowViewModel.IsMetaOnlyEditType"/> so
    /// the rule lives in one place (the row VM) and both ConfirmTx and the dialog XAML
    /// (<see cref="IsEditingMetaOnly"/>) agree on what "meta-only" means.
    /// </summary>
    private static bool IsMetaOnlyEdit(TradeRowViewModel row) => row.IsMetaOnlyEditType;

    /// <summary>
    /// In-place UPDATE of an existing trade — used for Sell edits and legacy unlinked
    /// Buy/StockDividend edits. Preserves the trade Id (and therefore <see cref="Trade.PortfolioEntryId"/>
    /// and any downstream references). Only date and note are considered editable; all
    /// economic fields (price, quantity, cash amount) are copied from the original row so
    /// balances don't need to be re-reconciled.
    /// </summary>
    private async Task UpdateTradeMetaOnlyAsync(TradeRowViewModel oldRow)
    {
        try
        {
            var updated = await _tradeMetadataWorkflowService.UpdateAsync(new TradeMetadataUpdateRequest(
                    oldRow.Id,
                    DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime(),
                    string.IsNullOrWhiteSpace(TxNote) ? null : TxNote))
                .ConfigureAwait(true);
            if (!updated)
                return;
            CloseTxDialog();
            await _loadTradesAsync();
            TransactionCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            TxError = $"更新失敗：{ex.Message}";
        }
    }

    private async Task ConfirmBuyAsync()
    {
        // Delegate to the AddAssetDialog sub-VM's buy logic based on asset sub-type.
        // AddAssetType must match TxBuyAssetType so ConfirmAdd routes to the correct add path.
        // The two dialogs are mutually exclusive in practice; this is an acceptable coupling.
        AddAssetDialog.AddAssetType = TxBuyAssetType;
        AddAssetDialog.AddError = string.Empty;

        await AddAssetDialog.ConfirmAddCommand.ExecuteAsync(null);

        // Propagate error to TxError for display in the Tx dialog
        if (!string.IsNullOrEmpty(AddAssetDialog.AddError))
        {
            TxError = AddAssetDialog.AddError;
            return;
        }

        // Close Tx dialog (the sub-methods may have closed AddDialog already)
        AddAssetDialog.IsAddDialogOpen = false;
        IsTxDialogOpen = false;
        // TransactionCompleted will be raised by ConfirmTx after this returns (no error)
    }

    private async Task ConfirmSellTxAsync()
    {
        if (TxSellPosition is null)
        { TxError = "請選擇持倉"; return; }

        if (!ParseHelpers.TryParseInt(TxSellQuantity, out var sellQty) || sellQty <= 0)
        { TxError = "賣出數量無效"; return; }
        if (sellQty > (int)TxSellPosition.Quantity)
        { TxError = $"賣出數量 ({sellQty:N0}) 超過持倉 ({(int)TxSellPosition.Quantity:N0}) 股"; return; }

        if (!ParseHelpers.TryParseDecimal(TxAmount, out var sellPrice) || sellPrice <= 0)
        { TxError = "賣出價格無效"; return; }

        var error = await SellPanel.ExecuteSellFromTxDialogAsync(
            row: TxSellPosition,
            sellPrice: sellPrice.ToString(),
            cashAccount: TxUseCashAccount ? TxCashAccount : null,
            isSellEtf: _search.IsEtf(TxSellPosition.Symbol),
            qtyOverride: sellQty);

        if (error is not null)
        {
            TxError = error;
            return;
        }

        IsTxDialogOpen = false;
        // TransactionCompleted will be raised by ConfirmTx after this returns (no error)
    }

    private async Task ConfirmIncomeAsync()
    {
        if (!ParseHelpers.TryParseDecimal(TxAmount, out var amount) || amount <= 0)
        { TxError = "金額無效"; return; }
        var fee = ParseOptionalFee(out var feeError);
        if (feeError is not null)
        { TxError = feeError; return; }

        var cashAccId = await ResolveCashAccountIdAsync();
        var tradeDate = DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime();
        await _transactionWorkflowService.RecordIncomeAsync(new IncomeTransactionRequest(
            amount,
            tradeDate,
            cashAccId,
            TxNote,
            fee,
            TxCategoryId));

        await _reloadAccountBalancesAsync();
        CloseTxDialog();
        await _loadTradesAsync();
        // TransactionCompleted raised by ConfirmTx
    }

    /// <summary>
    /// 現金股利 → 連動現金帳戶。支援兩種輸入模式：
    /// <list type="bullet">
    /// <item><description><b>perShare</b>：填每股股利 → total = perShare × 持股數</description></item>
    /// <item><description><b>total</b>：直接填總股息金額 → perShare = total / 持股數（用於存進 Trade.Price）</description></item>
    /// </list>
    /// 可選填手續費（如二代健保補充費），會額外建一筆 Withdrawal 連動現金帳戶。
    /// </summary>
    private async Task ConfirmCashDivAsync()
    {
        if (TxDivPosition is null)
        { TxError = "請選擇股票"; return; }

        decimal perShare;
        decimal total;
        if (TxDivIsTotalMode)
        {
            if (!ParseHelpers.TryParseDecimal(TxDivTotalInput, out total) || total <= 0)
            { TxError = "總股息金額無效"; return; }
            perShare = TxDivPosition.Quantity > 0 ? total / TxDivPosition.Quantity : 0;
        }
        else
        {
            if (!ParseHelpers.TryParseDecimal(TxDivPerShare, out perShare) || perShare <= 0)
            { TxError = "每股股利無效"; return; }
            total = perShare * TxDivPosition.Quantity;
        }

        var fee = ParseOptionalFee(out var feeError);
        if (feeError is not null)
        { TxError = feeError; return; }

        var divName = string.IsNullOrEmpty(TxDivPosition.Name)
                      ? TxDivPosition.Symbol
                      : TxDivPosition.Name;
        var tradeDate = DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime();
        var cashAccId = TxUseCashAccount ? await ResolveCashAccountIdAsync() : null;
        await _transactionWorkflowService.RecordCashDividendAsync(new CashDividendTransactionRequest(
            TxDivPosition.Symbol,
            TxDivPosition.Exchange,
            divName,
            perShare,
            (int)TxDivPosition.Quantity,
            total,
            tradeDate,
            cashAccId,
            fee));

        await _reloadAccountBalancesAsync();
        CloseTxDialog();
        await _loadTradesAsync();
        // TransactionCompleted raised by ConfirmTx
    }

    private async Task ConfirmStockDivAsync()
    {
        if (TxStockDivPosition is null)
        { TxError = "請選擇股票"; return; }
        if (!ParseHelpers.TryParseInt(TxStockDivNewShares, out var newShares) || newShares <= 0)
        { TxError = "配股數無效"; return; }

        var divName = string.IsNullOrEmpty(TxStockDivPosition.Name)
                      ? TxStockDivPosition.Symbol
                      : TxStockDivPosition.Name;
        await _transactionWorkflowService.RecordStockDividendAsync(new StockDividendTransactionRequest(
            TxStockDivPosition.Symbol,
            TxStockDivPosition.Exchange,
            divName,
            newShares,
            DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime(),
            TxStockDivPosition.Id));

        CloseTxDialog();
        await _loadTradesAsync();
        // TransactionCompleted raised by ConfirmTx
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

        await _reloadAccountBalancesAsync();
        CloseTxDialog();
        await _loadTradesAsync();
        // TransactionCompleted raised by ConfirmTx
    }

    // Shared fee helpers

    /// <summary>
    /// 解析 <see cref="TxFee"/> 為正小數；空白/0 視為無手續費（回 0、err 為 null）。
    /// 負數或非數字回傳 null fee + err 訊息，呼叫端應 set TxError 並 return。
    /// </summary>
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

        TxPrincipal = next.PrincipalAmount.ToString("F0");
        TxInterestPaid = next.InterestAmount > 0 ? next.InterestAmount.ToString("F0") : string.Empty;
    }

    private decimal ParseOptionalFee(out string? err)
    {
        err = null;
        if (string.IsNullOrWhiteSpace(TxFee))
            return 0m;
        if (!ParseHelpers.TryParseDecimal(TxFee, out var fee) || fee < 0)
        {
            err = "手續費無效";
            return 0m;
        }
        return fee;
    }

    /// <summary>
    /// 借款/還款 → 負債帳戶與現金帳戶同步調整；可選填手續費（AscentPortfolio pattern）。
    /// </summary>
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
            if (!ParseHelpers.TryParseDecimal(TxPrincipal, out var p) || p < 0)
            { TxError = "本金無效"; return; }
            var rawInterest = string.IsNullOrWhiteSpace(TxInterestPaid) ? "0" : TxInterestPaid;
            if (!ParseHelpers.TryParseDecimal(rawInterest, out var ip) || ip < 0)
            { TxError = "利息金額無效"; return; }
            if (p + ip <= 0)
            { TxError = "本金與利息合計不得為零"; return; }
            principal = p;
            interestPaid = ip > 0 ? ip : null;
            cashAmount = p + ip;
        }

        if (string.IsNullOrWhiteSpace(TxLoanLabel))
        { TxError = "請輸入或選擇貸款名稱"; return; }

        // ── 攤還表欄位（選填；借款時才適用）────────────────────────────────
        decimal? amortAnnualRate = null;
        int? amortTermMonths = null;
        if (type == TradeType.LoanBorrow &&
            !string.IsNullOrWhiteSpace(TxLoanRate) &&
            !string.IsNullOrWhiteSpace(TxLoanTermMonths))
        {
            if (!ParseHelpers.TryParseDecimal(TxLoanRate, out var ratePct) || ratePct < 0)
            { TxError = "年利率無效"; return; }
            if (!ParseHelpers.TryParseInt(TxLoanTermMonths, out var termMo) || termMo <= 0)
            { TxError = "還款期數無效"; return; }
            amortAnnualRate = ratePct / 100m;
            amortTermMonths = termMo;
        }

        var fee = ParseOptionalFee(out var feeError);
        if (feeError is not null)
        { TxError = feeError; return; }

        var tradeDate = DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime();
        var liabName = TxLoanLabel.Trim();
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
            amortAnnualRate.HasValue && amortTermMonths.HasValue ? DateOnly.FromDateTime(TxLoanStartDate) : null));

        await _reloadAccountBalancesAsync();
        CloseTxDialog();
        await _loadTradesAsync();

        if (amortAnnualRate.HasValue)
        {
            await _loadLiabilitiesAsync();
            _rebuildTotals();
        }
        // TransactionCompleted raised by ConfirmTx
    }

    private async Task ConfirmCreditCardChargeAsync()
    {
        if (TxCreditCard?.AssetId is not { } cardId)
        { TxError = "請選擇信用卡"; return; }
        if (!ParseHelpers.TryParseDecimal(TxAmount, out var amount) || amount <= 0)
        { TxError = "金額無效"; return; }

        var tradeDate = DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime();
        await _creditCardTransactionWorkflowService.ChargeAsync(new CreditCardChargeRequest(
            cardId,
            TxCreditCard.Label,
            tradeDate,
            amount,
            string.IsNullOrWhiteSpace(TxNote) ? null : TxNote));

        await _reloadAccountBalancesAsync();
        CloseTxDialog();
        await _loadTradesAsync();
    }

    private async Task ConfirmCreditCardPaymentAsync()
    {
        if (TxCreditCard?.AssetId is not { } cardId)
        { TxError = "請選擇信用卡"; return; }
        if (!ParseHelpers.TryParseDecimal(TxAmount, out var amount) || amount <= 0)
        { TxError = "金額無效"; return; }
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
            TxCreditCard.Label,
            cashAccId.Value,
            accountName,
            tradeDate,
            amount,
            string.IsNullOrWhiteSpace(TxNote) ? null : TxNote));

        await _reloadAccountBalancesAsync();
        CloseTxDialog();
        await _loadTradesAsync();
    }

    /// <summary>
    /// 轉帳 → 在兩個現金帳戶之間搬錢。
    /// </summary>
    private async Task ConfirmTransferAsync()
    {
        if (!ParseHelpers.TryParseDecimal(TxAmount, out var srcAmount) || srcAmount <= 0)
        { TxError = "轉出金額無效"; return; }
        if (!ParseHelpers.TryParseDecimal(TxTransferTargetAmount, out var dstAmount) || dstAmount <= 0)
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
        var dstName = TxTransferTarget?.Name ?? TxTransferTargetName.Trim();
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

        await _reloadAccountBalancesAsync();
        CloseTxDialog();
        await _loadTradesAsync();
        // TransactionCompleted raised by ConfirmTx
    }

    private static TradeDeletionRequest ToTradeDeletionRequest(TradeRowViewModel row) =>
        new(row.Id, row.Type, row.Symbol, row.Quantity, row.PortfolioEntryId);
}
