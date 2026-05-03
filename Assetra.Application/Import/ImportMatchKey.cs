using System.Globalization;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;

namespace Assetra.Application.Import;

/// <summary>
/// 與 DB 對照的去重鍵。<br/>
/// 銀行明細使用：日期 + 金額（絕對值取兩位小數）。<br/>
/// 券商明細使用：日期 + 標的 + 買賣方向 + 股數 + 每股成交價。<br/>
/// 這樣可避免同日同標的多筆成交、或買賣金額剛好相同時被錯判為重複。
/// </summary>
internal static class ImportMatchKey
{
    public static string FromPreview(ImportPreviewRow row, Guid? cashAccountId = null) =>
        IsBrokerRow(row)
            ? BuildBroker(
                row.Date,
                row.Symbol,
                row.Quantity!.Value,
                ResolvePreviewDirection(row),
                ResolvePreviewPrice(row),
                cashAccountId)
            : BuildBank(row.Date, row.Amount, cashAccountId);

    public static string FromTrade(Trade trade)
    {
        var date = DateOnly.FromDateTime(trade.TradeDate);
        return trade.Type switch
        {
            TradeType.Buy or TradeType.Sell => BuildBroker(
                date,
                trade.Symbol,
                trade.Quantity,
                trade.Type == TradeType.Sell ? "SELL" : "BUY",
                trade.Price,
                trade.CashAccountId),
            _ => BuildBank(date, trade.CashAmount ?? 0m, trade.CashAccountId, DirectionForTrade(trade)),
        };
    }

    private static bool IsBrokerRow(ImportPreviewRow row) =>
        !string.IsNullOrWhiteSpace(row.Symbol) && row.Quantity is > 0m;

    private static string BuildBank(DateOnly date, decimal amount, Guid? cashAccountId, string? direction = null) =>
        string.Join('|',
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            cashAccountId?.ToString("N") ?? string.Empty,
            direction ?? (amount >= 0m ? "IN" : "OUT"),
            Math.Abs(amount).ToString("0.00", CultureInfo.InvariantCulture));

    private static string BuildBroker(
        DateOnly date,
        string? symbol,
        decimal quantity,
        string direction,
        decimal unitPrice,
        Guid? cashAccountId) =>
        string.Join('|',
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            cashAccountId?.ToString("N") ?? string.Empty,
            string.IsNullOrWhiteSpace(symbol) ? string.Empty : symbol.Trim().ToUpperInvariant(),
            direction,
            quantity.ToString("0.####", CultureInfo.InvariantCulture),
            Math.Abs(unitPrice).ToString("0.####", CultureInfo.InvariantCulture));

    private static string DirectionForTrade(Trade trade) =>
        trade.Type switch
        {
            TradeType.Income or TradeType.Deposit or TradeType.Sell or TradeType.CashDividend or TradeType.LoanBorrow => "IN",
            _ => "OUT",
        };

    private static string ResolvePreviewDirection(ImportPreviewRow row) =>
        (row.Counterparty ?? string.Empty).Contains("賣", StringComparison.Ordinal)
        || (row.Counterparty ?? string.Empty).Contains("Sell", StringComparison.OrdinalIgnoreCase)
            ? "SELL"
            : "BUY";

    private static decimal ResolvePreviewPrice(ImportPreviewRow row)
    {
        if (row.UnitPrice is { } explicitPrice && explicitPrice > 0m)
            return explicitPrice;

        if (row.Quantity is not { } qty || qty <= 0m)
            return 0m;

        var isSell = ResolvePreviewDirection(row) == "SELL";
        var commission = row.Commission ?? 0m;
        var grossTradeAmount = isSell
            ? row.Amount + commission
            : row.Amount - commission;
        return grossTradeAmount / qty;
    }
}
