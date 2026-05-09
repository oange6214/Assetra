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
        // Delegate to the AddAssetDialog sub-VM's buy logic based on asset sub-type.
        // AddAssetType must match Buy.AssetType so ConfirmAdd routes to the correct add path.
        // The two dialogs are mutually exclusive in practice; this is an acceptable coupling.
        AddAssetDialog.AddAssetType = Buy.AssetType;
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
        if (Sell.Position is null)
        { TxError = "請選擇持倉"; return; }

        if (!ParseHelpers.TryParseInt(Sell.Quantity, out var sellQty) || sellQty <= 0)
        { TxError = "賣出數量無效"; return; }
        if (sellQty > (int)Sell.Position.Quantity)
        { TxError = $"賣出數量 ({sellQty:N0}) 超過持倉 ({(int)Sell.Position.Quantity:N0}) 股"; return; }

        if (!ParseHelpers.TryParseDecimal(TxAmount, out var sellPrice) || sellPrice <= 0)
        { TxError = "賣出價格無效"; return; }

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
