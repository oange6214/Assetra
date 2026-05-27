using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Models;
using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// Confirm.cs split — Cash + Stock dividend confirmation.
/// </summary>
public partial class TransactionDialogViewModel
{
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
        if (Div.Position is null)
        { TxError = "請選擇股票"; return; }

        decimal perShare;
        decimal total;
        if (Div.IsTotalMode)
        {
            if (!ParseHelpers.TryParseDecimal(Div.TotalInput, out total) || total <= 0)
            { TxError = "總股息金額無效"; return; }
            perShare = Div.Position.Quantity > 0 ? total / Div.Position.Quantity : 0;
        }
        else
        {
            if (!ParseHelpers.TryParseDecimal(Div.PerShare, out perShare) || perShare <= 0)
            { TxError = "每股股利無效"; return; }
            total = perShare * Div.Position.Quantity;
        }

        var fee = ParseOptionalFee(out var feeError);
        if (feeError is not null)
        { TxError = feeError; return; }

        // P5.8b prereq — mode-aware settlement validation mirrors Buy + Sell.
        // The "雙保險自動反推" path below still runs (so the saved trade has
        // both fields when only one is user-entered), but the mode toggle now
        // decides which input is *required* on cross-currency dividends.
        if (Div.IsCrossCurrency)
        {
            if (Div.IsStatementSettlementMode && string.IsNullOrWhiteSpace(Div.ActualCashAmount))
            { TxError = "跨幣別股息請填寫實際入帳金額（明細模式），或切換為匯率估算"; return; }
            if (Div.IsFxSettlementMode && string.IsNullOrWhiteSpace(Div.FxRate))
            { TxError = "跨幣別股息請填寫或取得匯率（估算模式），或切換為明細金額"; return; }
        }

        // MultiCurrency-Trade-Refactor P3 — parse optional cross-currency fields.
        // 同幣別股息（如台股配 TWD 股息入 TWD 帳戶）兩者皆空。
        decimal? divActualCash = null;
        if (!string.IsNullOrWhiteSpace(Div.ActualCashAmount))
        {
            if (!ParseHelpers.TryParseDecimal(Div.ActualCashAmount, out var parsedActual) || parsedActual <= 0)
            { TxError = "實際入帳金額無效"; return; }
            divActualCash = parsedActual;
        }
        decimal? divFxRate = null;
        if (!string.IsNullOrWhiteSpace(Div.FxRate))
        {
            if (!ParseHelpers.TryParseDecimal(Div.FxRate, out var parsedFx) || parsedFx <= 0)
            { TxError = "匯率無效"; return; }
            divFxRate = parsedFx;
        }

        // P3 — 雙保險自動反推：擇一填寫即可，存兩者方便未來報表還原。
        // total = perShare × Quantity（標的幣別）。
        if (divActualCash is null && divFxRate is { } fxOnly && total > 0)
        {
            divActualCash = total * fxOnly;
        }
        else if (divFxRate is null && divActualCash is { } cashOnly && total > 0)
        {
            divFxRate = cashOnly / total;
        }

        var divName = string.IsNullOrEmpty(Div.Position.Name)
                      ? Div.Position.Symbol
                      : Div.Position.Name;
        var tradeDate = DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime();
        var cashAccId = TxUseCashAccount ? await ResolveCashAccountIdAsync() : null;
        await _transactionWorkflowService.RecordCashDividendAsync(new CashDividendTransactionRequest(
            Div.Position.Symbol,
            Div.Position.Exchange,
            divName,
            perShare,
            (int)Div.Position.Quantity,
            total,
            tradeDate,
            cashAccId,
            fee,
            divActualCash,
            divFxRate,
            SelectedPortfolioGroup?.Id));

        await AfterTxSuccessAsync();
    }

    private async Task ConfirmStockDivAsync()
    {
        if (Div.StockPosition is null)
        { TxError = "請選擇股票"; return; }
        if (!ParseHelpers.TryParseInt(Div.StockNewShares, out var newShares) || newShares <= 0)
        { TxError = "配股數無效"; return; }

        var divName = string.IsNullOrEmpty(Div.StockPosition.Name)
                      ? Div.StockPosition.Symbol
                      : Div.StockPosition.Name;
        await _transactionWorkflowService.RecordStockDividendAsync(new StockDividendTransactionRequest(
            Div.StockPosition.Symbol,
            Div.StockPosition.Exchange,
            divName,
            newShares,
            DateTime.SpecifyKind(TxDate, DateTimeKind.Local).ToUniversalTime(),
            Div.StockPosition.Id));

        await AfterTxSuccessAsync(reloadBalances: false);
    }
}
