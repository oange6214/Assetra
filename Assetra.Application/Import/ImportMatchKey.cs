using System.Globalization;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;

namespace Assetra.Application.Import;

/// <summary>
/// 與 DB 對照的「寬鬆」去重鍵：日期 + 金額（絕對值取兩位小數）+ 標的代碼。<br/>
/// 不含對手方 / 摘要——既有 <see cref="Trade"/> 不一定有對應欄位。
/// </summary>
internal static class ImportMatchKey
{
    public static string FromPreview(ImportPreviewRow row) =>
        Build(row.Date, row.Amount, row.Symbol);

    public static string FromTrade(Trade trade)
    {
        var date = DateOnly.FromDateTime(trade.TradeDate);
        var amount = ResolveAmount(trade);
        return Build(date, amount, trade.Symbol);
    }

    private static string Build(DateOnly date, decimal amount, string? symbol) =>
        string.Join('|',
            date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Math.Abs(amount).ToString("0.00", CultureInfo.InvariantCulture),
            string.IsNullOrWhiteSpace(symbol) ? string.Empty : symbol.Trim().ToUpperInvariant());

    private static decimal ResolveAmount(Trade trade) => trade.Type switch
    {
        TradeType.Buy or TradeType.Sell => trade.Price * trade.Quantity,
        _ => trade.CashAmount ?? 0m,
    };
}
