using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Models;
using Assetra.Core.Trading;
using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.Portfolio;

internal sealed class PortfolioSellPanelController
{
    public SellPanelPreviewState BuildPreview(
        PortfolioRowViewModel? row,
        string sellPriceInput,
        decimal commissionDiscount,
        bool isSellEtf)
    {
        if (row is null || !ParseHelpers.TryParseDecimal(sellPriceInput, out var price) || price <= 0)
            return SellPanelPreviewState.Empty;

        var quantity = (int)row.Quantity;
        var fee = TaiwanTradeFeeCalculator.CalcSell(
            price,
            quantity,
            commissionDiscount,
            isSellEtf,
            row.IsBondEtf);

        var estimatedPnl = fee.NetAmount - row.BuyPrice * quantity;
        return new SellPanelPreviewState(
            fee.GrossAmount,
            fee.Commission,
            fee.TransactionTax,
            fee.NetAmount,
            estimatedPnl,
            estimatedPnl >= 0);
    }

    public SellPanelSubmitState BuildSubmission(
        PortfolioRowViewModel? row,
        string sellPriceInput,
        string manualFeeInput,
        int sellQtyOverride,
        DateTime tradeDate,
        decimal commissionDiscount,
        bool isSellEtf,
        Guid? cashAccountId)
    {
        if (row is null)
            return new SellPanelSubmitState(null, "賣出標的不存在");

        if (!ParseHelpers.TryParseDecimal(sellPriceInput, out var sellPrice) || sellPrice <= 0)
            return new SellPanelSubmitState(null, "賣出價格無效");

        var currentQty = (int)row.Quantity;
        var sellQty = sellQtyOverride > 0 ? sellQtyOverride : currentQty;
        if (currentQty <= 0)
            return new SellPanelSubmitState(null, "持倉數量無效");
        if (sellQty <= 0)
            return new SellPanelSubmitState(null, "賣出數量無效");
        if (sellQty > currentQty)
            return new SellPanelSubmitState(null, $"賣出數量 ({sellQty:N0}) 超過持倉 ({currentQty:N0}) 股");

        decimal sellCommission;
        decimal? sellDiscount;
        if (!string.IsNullOrWhiteSpace(manualFeeInput))
        {
            if (!ParseHelpers.TryParseDecimal(manualFeeInput, out var manualFee) || manualFee < 0)
                return new SellPanelSubmitState(null, "手續費無效");

            sellCommission = manualFee;
            sellDiscount = null;
        }
        else
        {
            var feeResult = TaiwanTradeFeeCalculator.CalcSell(
                sellPrice,
                sellQty,
                commissionDiscount,
                isSellEtf,
                row.IsBondEtf);
            sellCommission = feeResult.Commission + feeResult.TransactionTax;
            sellDiscount = commissionDiscount;
        }

        return new SellPanelSubmitState(
            new SellWorkflowRequest(
                row.Id,
                row.Symbol,
                row.Exchange,
                row.Name,
                row.BuyPrice,
                currentQty,
                sellQty,
                sellPrice,
                tradeDate,
                sellCommission,
                sellDiscount,
                cashAccountId,
                row.AllEntryIds),
            null);
    }
}

internal sealed record SellPanelPreviewState(
    decimal GrossAmount,
    decimal Commission,
    decimal TransactionTax,
    decimal NetAmount,
    decimal EstimatedPnl,
    bool IsEstimatedPositive)
{
    public static SellPanelPreviewState Empty { get; } = new(0, 0, 0, 0, 0, false);
}

internal sealed record SellPanelSubmitState(
    SellWorkflowRequest? Request,
    string? Error);
