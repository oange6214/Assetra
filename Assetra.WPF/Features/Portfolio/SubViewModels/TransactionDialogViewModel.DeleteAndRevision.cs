using Assetra.Application.Portfolio.Dtos;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Delete + revision-replace command surface for the Tx dialog. Split out
/// from <c>TransactionDialogViewModel.cs</c> (M6: file size cleanup) so the
/// main file holds primarily field declarations and the command logic for
/// deletion / revision-replace lives next to its semantically-related siblings.
/// </summary>
public partial class TransactionDialogViewModel
{
    [RelayCommand(CanExecute = nameof(IsEditMode))]
    private void CreateRevision()
    {
        if (EditingTradeId is not { } id)
            return;

        var row = Trades.FirstOrDefault(t => t.Id == id);
        if (row is null)
            return;

        IsRevisionMode = true;
        _revisionSourceTradeId = row.Id;
        IsRevisionReplacePromptOpen = false;
        RevisionReplacePromptError = string.Empty;
        EditingTradeId = null;
        TxError = string.Empty;
        EditSummaryType = string.Empty;
        EditSummaryTarget = string.Empty;
        EditSummaryAmount = string.Empty;
        EditSummaryQuantity = string.Empty;
        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(IsEditingMetaOnly));
        OnPropertyChanged(nameof(AreEconomicFieldsEditable));
        OnPropertyChanged(nameof(ShowEditLockedSummary));
        OnPropertyChanged(nameof(ShowTxCurrencyRow));
        CreateRevisionCommand.NotifyCanExecuteChanged();
        DeleteTradeCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 刪除目前正在編輯的交易。只在 <see cref="IsEditMode"/> 為 true 時可執行。
    /// Buy / Sell / StockDividend 會同步清理關聯的 <see cref="PortfolioEntry"/>。
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsEditMode))]
    private void RequestDeleteTrade()
    {
        if (EditingTradeId is not { } id)
            return;

        var row = Trades.FirstOrDefault(t => t.Id == id);
        DeleteTargetName = row?.DisplayAsset ?? EditSummaryTarget;
        IsDeleteConfirmOpen = true;
    }

    [RelayCommand]
    private void CancelDeleteTrade()
    {
        IsDeleteConfirmOpen = false;
        DeleteTargetName = string.Empty;
    }

    [RelayCommand]
    private async Task ConfirmDeleteTradeAsync()
    {
        IsDeleteConfirmOpen = false;
        await DeleteTradeAsync();
        DeleteTargetName = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(IsEditMode))]
    private async Task DeleteTradeAsync()
    {
        if (EditingTradeId is not { } id)
            return;

        var row = Trades.FirstOrDefault(t => t.Id == id);
        if (row is null)
            return;

        try
        {
            var result = await _tradeDeletionWorkflowService.DeleteAsync(ToTradeDeletionRequest(row));
            if (!result.Success && result.BlockedBySell)
            {
                TxError = L("Portfolio.Trade.DeleteBlockedBySell",
                    "請先刪除此股票的賣出記錄，再刪除此買入記錄。");
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete trade {TradeId}", id);
            _snackbar?.Error(L("Portfolio.Trade.OldDeleteFailed", "交易刪除失敗"));
            return;
        }

        CloseTxDialog();
        TradeDeleted?.Invoke(this, EventArgs.Empty);
    }


    [RelayCommand]
    private void KeepBothRecords()
    {
        _revisionSourceTradeId = null;
        IsRevisionReplacePromptOpen = false;
        RevisionReplacePromptError = string.Empty;
        CloseTxDialog();
        TransactionCompleted?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ReplaceOriginalRecordAsync()
    {
        if (_revisionSourceTradeId is not { } sourceId)
            return;

        var row = Trades.FirstOrDefault(t => t.Id == sourceId);
        if (row is null)
        {
            RevisionReplacePromptError = L("Portfolio.Record.ReplaceMissing", "找不到原紀錄，請重新整理後再試。");
            return;
        }

        try
        {
            // EditReplace: this is the "建立修正版 → 取代原紀錄" path, conceptually
            // an edit even though the user explicitly chose to replace. Audit log
            // gets "edit-replace" so it groups with the implicit edit flow.
            var result = await _tradeDeletionWorkflowService.DeleteAsync(
                ToTradeDeletionRequest(row, TradeDeletionReason.EditReplace));
            if (!result.Success && result.BlockedBySell)
            {
                RevisionReplacePromptError = L("Portfolio.Trade.DeleteBlockedBySell",
                    "請先刪除此股票的賣出記錄，再刪除此買入記錄。");
                return;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to replace original trade {TradeId} after revision", sourceId);
            RevisionReplacePromptError = L("Portfolio.Record.ReplaceFailed", "刪除原紀錄失敗，修正版已保留。");
            return;
        }

        _revisionSourceTradeId = null;
        IsRevisionReplacePromptOpen = false;
        RevisionReplacePromptError = string.Empty;
        CloseTxDialog();
        TransactionCompleted?.Invoke(this, EventArgs.Empty);
    }
}
