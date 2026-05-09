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
    /// <summary>
    /// Strategy-pattern dispatch for ConfirmTx. Maps TxType string → per-type
    /// Confirm handler. Adding a new TxType is one entry; the dispatcher core
    /// stays unchanged.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Func<TransactionDialogViewModel, Task>>
        _confirmDispatch = new Dictionary<string, Func<TransactionDialogViewModel, Task>>(StringComparer.Ordinal)
        {
            ["income"]             = vm => vm.ConfirmIncomeAsync(),
            ["cashDiv"]            = vm => vm.ConfirmCashDivAsync(),
            ["stockDiv"]           = vm => vm.ConfirmStockDivAsync(),
            ["deposit"]            = vm => vm.ConfirmCashFlowAsync(TradeType.Deposit),
            ["withdrawal"]         = vm => vm.ConfirmCashFlowAsync(TradeType.Withdrawal),
            ["loanBorrow"]         = vm => vm.ConfirmLoanAsync(TradeType.LoanBorrow),
            ["loanRepay"]          = vm => vm.ConfirmLoanAsync(TradeType.LoanRepay),
            ["creditCardCharge"]   = vm => vm.ConfirmCreditCardChargeAsync(),
            ["creditCardPayment"]  = vm => vm.ConfirmCreditCardPaymentAsync(),
            ["transfer"]           = vm => vm.ConfirmTransferAsync(),
            ["buy"]                = vm => vm.ConfirmBuyAsync(),
            ["sell"]               = vm => vm.ConfirmSellTxAsync(),
        };

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
        TradeRowViewModel? oldRow = pendingEditId is { } id
            ? Trades.FirstOrDefault(t => t.Id == id)
            : null;

        if (pendingEditId is { } editId && oldRow is null)
        {
            await _loadTradesAsync();
            oldRow = Trades.FirstOrDefault(t => t.Id == editId);
            if (oldRow is null)
            {
                TxError = L("Portfolio.Trade.EditTargetMissing",
                    "原交易記錄已不存在，交易清單已重新整理，請重新開啟最新紀錄再編輯。");
                _snackbar?.Warning(TxError);
                CloseTxDialog();
                return;
            }
        }

        // Clear EditingTradeId so downstream handlers (e.g. inline "which symbol is this
        // buy for" logic) don't mistake this for an in-progress edit loop.
        EditingTradeId = null;

        // Safe edit mode updates only metadata in-place so balances, positions, FIFO lots,
        // and paired ledger effects stay untouched. Full economic changes intentionally go
        // through Create Revision, which clears EditingTradeId and reaches the create-new path.
        if (oldRow is not null && !IsRevisionMode)
        {
            await UpdateTradeMetaOnlyAsync(oldRow);
            return;
        }

        var revisionSourceTradeId = _revisionSourceTradeId;
        _preserveRevisionSourceOnClose = revisionSourceTradeId.HasValue;

        try
        {
            // Strategy-pattern dispatch — replaces the 12-arm switch. Extension point
            // for future TxTypes is now adding one entry to _confirmDispatch.
            if (_confirmDispatch.TryGetValue(TxType, out var handler))
                await handler(this);
        }
        catch (Exception ex)
        {
            // Until now any exception thrown by a Confirm*Async or its downstream
            // service was swallowed by AsyncRelayCommand and the dialog closed
            // silently after CloseTxDialog had already run pre-throw — the user saw
            // "no record, no error".  Surface it loudly so the next failure is
            // diagnosable from the UI alone.
            Log.Error(ex, "Confirm transaction failed for TxType={TxType}", TxType);
            TxError = $"儲存失敗：{ex.Message}";
            _snackbar?.Error(TxError);
            EditingTradeId = pendingEditId;
            return;
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
            // Track delete failures so we can surface a persistent (non-snackbar)
            // error to the user. With both "old trade in ledger" + "new trade in
            // ledger" the projection double-counts — silently using a snackbar
            // alone would let the user keep working unaware of the inconsistency.
            var deleteFailed = false;
            try
            {
                // Pass EditReplace so the audit trail distinguishes this implicit
                // delete from an explicit user delete (manual delete = "delete",
                // edit flow = "edit-replace").
                var result = await _tradeDeletionWorkflowService.DeleteAsync(
                    ToTradeDeletionRequest(oldRow, TradeDeletionReason.EditReplace));
                if (!result.Success && result.BlockedBySell)
                {
                    TxError = L("Portfolio.Trade.DeleteBlockedBySell",
                        "請先刪除此股票的賣出記錄，再修改此買入記錄。");
                    EditingTradeId = pendingEditId;
                    return;
                }
                if (!result.Success)
                {
                    deleteFailed = true;
                    Log.Error("Old trade deletion returned Success=false during edit replace ({TradeId}) — possible ledger inconsistency", oldRow.Id);
                }
            }
            catch (Exception ex)
            {
                deleteFailed = true;
                // The new trade was already written; failing to delete the old one
                // leaves both records in the ledger so projections double-count.
                // Log the underlying error and keep the dialog open with a persistent
                // error banner (TxError) so the user knows manual reconciliation is
                // needed and doesn't silently miss the inconsistency.
                Log.Error(ex, "Failed to remove old trade {TradeId} during edit — possible duplicate entry", oldRow.Id);
            }

            // L1 perf: previously three back-to-back _loadService.LoadAsync
            // round-trips. ReloadAllAsync (when wired) does a single load and
            // applies every slice; falls back to the per-slice path for older
            // dependency configurations.
            if (_reloadAllAsync is not null)
            {
                await _reloadAllAsync();
            }
            else
            {
                await _loadPositionsAsync();
                await _loadTradesAsync();
                await _reloadAccountBalancesAsync();
            }
            _rebuildTotals();

            if (deleteFailed)
            {
                // Keep dialog open with a blocking error message so the user
                // can't silently miss the duplicate. Snackbar (transient) is
                // also fired for parity with the existing UX expectation.
                TxError = L("Portfolio.Trade.OldDeleteFailed",
                    "舊交易記錄刪除失敗，新紀錄已寫入但舊紀錄仍在 — 資料可能重複，請重新整理或刪除舊紀錄。");
                _snackbar?.Error(TxError);
                EditingTradeId = pendingEditId;
                return;
            }
        }

        TransactionCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// In-place UPDATE of an existing trade. Preserves the trade Id (and therefore
    /// <see cref="Trade.PortfolioEntryId"/> and any downstream references). Only date and
    /// note are editable in safe edit mode; all economic fields stay unchanged so balances
    /// don't need to be re-reconciled.
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
            {
                // L2: previously returned silently — dialog stayed open with no
                // visible feedback. Surface a localized "update failed" hint so
                // the user knows the click did something.
                TxError = L("Portfolio.Trade.MetaUpdateFailed",
                    "更新失敗，找不到此筆記錄或記錄已被修改。請關閉後重試。");
                return;
            }
            CloseTxDialog();
            await _loadTradesAsync();
            TransactionCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            TxError = string.Format(
                L("Portfolio.Trade.MetaUpdateError", "更新失敗：{0}"),
                ex.Message);
        }
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
    /// Builds a deletion request for the given row. The optional <paramref name="reason"/>
    /// drives the audit-log Action: defaults to <c>UserDelete</c> ("delete") for
    /// explicit user-initiated deletions; pass <c>EditReplace</c> from the
    /// edit-recreate flow so audit history can distinguish the two.
    /// </summary>
    private static TradeDeletionRequest ToTradeDeletionRequest(
        TradeRowViewModel row,
        TradeDeletionReason reason = TradeDeletionReason.UserDelete) =>
        new(row.Id, row.Type, row.Symbol, row.Quantity, row.PortfolioEntryId, reason);

    /// <summary>
    /// H2 consolidation: every successful Confirm method previously ended
    /// with a near-identical reload tail
    ///
    ///     await _reloadAccountBalancesAsync();
    ///     CloseTxDialog();
    ///     await _loadTradesAsync();
    ///
    /// (sometimes plus liabilities + totals for amortised loans).
    /// Repeated 7+ times across this file with subtle inconsistencies —
    /// CreditCard paths skipped certain reloads, Loan reloaded
    /// liabilities only when amortised. One helper, one exit point.
    /// </summary>
    private async Task AfterTxSuccessAsync(
        bool reloadBalances = true,
        bool reloadLiabilities = false,
        bool rebuildTotals = false)
    {
        if (reloadBalances)
            await _reloadAccountBalancesAsync();
        CloseTxDialog();
        await _loadTradesAsync();
        if (reloadLiabilities)
            await _loadLiabilitiesAsync();
        if (rebuildTotals)
            _rebuildTotals();
    }
}
