using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Confirm.cs split — Buy + Sell transaction confirmation. Buy delegates
/// to AddAssetDialogViewModel since the two dialogs share the asset-add
/// pipeline; Sell pipes through SellPanelViewModel.ExecuteSellFromTxDialogAsync.
/// </summary>
public partial class TransactionDialogViewModel
{
    private async Task ConfirmBuyAsync()
    {
        // 跨幣別買入若匯率尚未帶入,確認前先同步補抓一次當日匯率。改日期觸發的自動抓取是
        // fire-and-forget(QueueBuyFxRateRefresh),使用者「設好日期就按確認」時可能還沒回來;
        // 不先補抓的話,反推單價會卡在「需要匯率」(舊版甚至退回 1.0 把台幣值當 USD 存)。
        // 抓不到(例:該日無掛牌)才往下,由既有驗證以「查無此日期匯率/需要匯率」擋下。
        // 手動匯率時 FxRate 非空,不會進來、也不會被覆蓋。
        if (Buy.IsCrossCurrency && string.IsNullOrWhiteSpace(Buy.FxRate))
        {
            await RefreshBuyFxRateAsync(force: false).ConfigureAwait(true);
        }

        // Delegate to the AddAssetDialog sub-VM's buy logic based on asset sub-type.
        // AddAssetType must match Buy.AssetType so ConfirmAdd routes to the correct add path.
        // The two dialogs are mutually exclusive in practice; this is an acceptable coupling.
        AddAssetDialog.AddAssetType = Buy.AssetType;
        AddAssetDialog.AddError = string.Empty;
        // Portfolio-Groups-Refactor P3 — propagate group choice into the Buy request via AddAssetDialog.
        AddAssetDialog.SelectedPortfolioGroupId = SelectedPortfolioGroup?.Id;

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
        if (Sell.Position is null)
        { TxError = "請選擇持倉"; return; }

        if (!ParseHelpers.TryParseInt(Sell.Quantity, out var sellQty) || sellQty <= 0)
        { TxError = "賣出數量無效"; return; }
        if (sellQty > (int)Sell.Position.Quantity)
        { TxError = $"賣出數量 ({sellQty:N0}) 超過持倉 ({(int)Sell.Position.Quantity:N0}) 股"; return; }

        // 總額模式（GROSS）：成交總額 = 股數 × 單價 ⇒ 單價 = 總額 / 股數（手續費/證交稅仍由 gross 計）；
        // 單價模式直接用 TxAmount。
        decimal sellPrice;
        if (Sell.IsTotalMode)
        {
            if (!ParseHelpers.TryParseDecimal(Sell.TotalProceeds, out var sellTotal) || sellTotal <= 0)
            { TxError = "成交總額無效"; return; }
            sellPrice = sellTotal / sellQty;
        }
        else if (!ParseHelpers.TryParseDecimal(TxAmount, out sellPrice) || sellPrice <= 0)
        { TxError = "賣出價格無效"; return; }

        // P5.8b prereq — mode-aware settlement validation mirrors Buy's
        // AddAssetDialogViewModel L628-637. SellPanel still walks the existing
        // ActualCashAmount-or-FxRate auto-derive logic; this gate makes the
        // user's explicit mode choice authoritative when cross-currency.
        if (Sell.IsCrossCurrency)
        {
            if (Sell.IsStatementSettlementMode && string.IsNullOrWhiteSpace(Sell.ActualCashAmount))
            { TxError = "跨幣別賣出請填寫實際入帳金額（明細模式），或切換為匯率估算"; return; }
            if (Sell.IsFxSettlementMode && string.IsNullOrWhiteSpace(Sell.FxRate))
            { TxError = "跨幣別賣出請填寫或取得匯率（估算模式），或切換為明細金額"; return; }
        }

        var error = await SellPanel.ExecuteSellFromTxDialogAsync(
            row: Sell.Position,
            sellPrice: sellPrice.ToString(),
            tradeDate: DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime(),
            cashAccount: TxUseCashAccount ? TxCashAccount : null,
            isSellEtf: _search.IsEtf(Sell.Position.Symbol),
            qtyOverride: sellQty);

        if (error is not null)
        {
            TxError = error;
            return;
        }

        IsTxDialogOpen = false;
        // TransactionCompleted will be raised by ConfirmTx after this returns (no error)
    }
}
