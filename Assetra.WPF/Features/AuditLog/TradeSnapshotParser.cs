using System.Text.Json;
using Assetra.Core.Models;

namespace Assetra.WPF.Features.AuditLog;

/// <summary>
/// Safely deserialises a <c>trade_audit.TradeJson</c> snapshot into a strongly
/// typed <see cref="Trade"/>. Returns <c>null</c> on any parse failure so the
/// audit viewer falls back to raw-JSON display rather than crashing.
/// </summary>
internal static class TradeSnapshotParser
{
    public static Trade? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Trade>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Builds a single-line domain summary like
    /// <c>"0056 Buy · 5,000 股 @ $23.05 = $115,250"</c> for use in the
    /// master-list summary column. Falls back to <c>(無法解析)</c> on bad JSON.
    /// </summary>
    public static string Summarize(Trade? trade)
    {
        if (trade is null) return "(無法解析)";

        var head = string.IsNullOrWhiteSpace(trade.Symbol) ? trade.Type.ToString() : $"{trade.Symbol} {trade.Type}";

        return trade.Type switch
        {
            TradeType.Buy or TradeType.Sell or TradeType.StockDividend
                => $"{head} · {trade.Quantity:N0} 股 @ {trade.Price:N4}",
            TradeType.CashDividend
                => $"{head} · {trade.Quantity:N0} 股 × {trade.Price:N4} = {trade.CashAmount:N0}",
            TradeType.Income or TradeType.Deposit or TradeType.Withdrawal
                => $"{head} · {trade.CashAmount:N0}",
            TradeType.Transfer
                => $"Transfer · {trade.CashAmount:N0}",
            TradeType.LoanBorrow or TradeType.LoanRepay
                => $"{trade.Type} · {trade.LoanLabel} · {trade.CashAmount:N0}",
            TradeType.CreditCardCharge or TradeType.CreditCardPayment
                => $"{trade.Type} · {trade.CashAmount:N0}",
            _ => head,
        };
    }
}
