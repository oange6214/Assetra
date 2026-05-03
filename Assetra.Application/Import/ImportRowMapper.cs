using Assetra.Core.DomainServices;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;

namespace Assetra.Application.Import;

/// <summary>
/// 預設 mapper：依 <see cref="ImportSourceKind"/> 將 <see cref="ImportPreviewRow"/> 轉成 <see cref="Trade"/>。
/// v0.8 起若呼叫者傳入 <c>rules</c>，會用 <see cref="AutoCategorizationEngine"/> 對 row 的 Counterparty / Memo
/// 做比對，命中時帶入 <see cref="Trade.CategoryId"/>。傳 null/empty 等同 v0.7 行為。
/// </summary>
public sealed class ImportRowMapper : IImportRowMapper
{
    public Trade? Map(
        ImportPreviewRow row,
        ImportSourceKind kind,
        ImportApplyOptions options,
        IList<string> warnings,
        IReadOnlyList<AutoCategorizationRule>? rules = null,
        IReadOnlyList<ExpenseCategory>? categories = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(warnings);

        var date = row.Date.ToDateTime(TimeOnly.MinValue);
        var note = ComposeNote(row, kind, options);

        var trade = kind switch
        {
            ImportSourceKind.BankStatement => MapBankRow(row, date, note, options),
            ImportSourceKind.BrokerStatement => MapBrokerRow(row, date, note, options, warnings),
            _ => null,
        };

        if (trade is not null && rules is { Count: > 0 } && trade.CategoryId is null)
        {
            var eligibleRules = categories is { Count: > 0 }
                ? AutoCategorizationRuleFilter.ForTradeType(rules, categories, trade.Type)
                : rules;
            if (eligibleRules.Count == 0)
                return trade;

            var ctx = new AutoCategorizationContext(
                Note: null,
                Counterparty: row.Counterparty,
                Memo: row.Memo,
                Source: AutoCategorizationScope.Import);
            var matched = AutoCategorizationEngine.Match(ctx, eligibleRules);
            if (matched is not null) trade = trade with { CategoryId = matched };
        }
        return trade;
    }

    private static Trade MapBankRow(ImportPreviewRow row, DateTime date, string note, ImportApplyOptions options)
    {
        var type = row.Amount >= 0 ? TradeType.Income : TradeType.Withdrawal;
        return new Trade(
            Id: Guid.NewGuid(),
            Symbol: string.Empty,
            Exchange: string.Empty,
            Name: string.Empty,
            Type: type,
            TradeDate: date,
            Price: 0m,
            Quantity: 1,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: Math.Abs(row.Amount),
            CashAccountId: options.CashAccountId,
            Note: note);
    }

    private static Trade? MapBrokerRow(
        ImportPreviewRow row,
        DateTime date,
        string note,
        ImportApplyOptions options,
        IList<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(row.Symbol) || row.Quantity is not { } qty || qty <= 0m)
        {
            warnings.Add($"Row {row.RowIndex}: missing symbol or quantity, skipped.");
            return null;
        }

        var quantityInt = (int)Math.Round(qty);
        var isSell = IsSell(row.Counterparty);
        var price = ResolveBrokerUnitPrice(row, quantityInt, isSell);

        return new Trade(
            Id: Guid.NewGuid(),
            Symbol: row.Symbol!.Trim(),
            Exchange: options.Exchange,
            Name: row.Symbol!.Trim(),
            Type: isSell ? TradeType.Sell : TradeType.Buy,
            TradeDate: date,
            Price: Math.Abs(price),
            Quantity: quantityInt,
            RealizedPnl: null,
            RealizedPnlPct: null,
            Commission: row.Commission,
            CashAccountId: options.CashAccountId,
            Note: note);
    }

    private static decimal ResolveBrokerUnitPrice(ImportPreviewRow row, int quantity, bool isSell)
    {
        if (row.UnitPrice is { } explicitPrice && explicitPrice > 0m)
            return explicitPrice;

        var commission = row.Commission ?? 0m;
        var grossTradeAmount = isSell
            ? row.Amount + commission
            : row.Amount - commission;

        if (quantity <= 0) return 0m;
        return grossTradeAmount / quantity;
    }

    private static bool IsSell(string? directionText) =>
        (directionText ?? string.Empty).Contains("賣", StringComparison.Ordinal)
        || (directionText ?? string.Empty).Contains("Sell", StringComparison.OrdinalIgnoreCase);

    private static string ComposeNote(ImportPreviewRow row, ImportSourceKind kind, ImportApplyOptions options)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.Counterparty)) parts.Add(row.Counterparty!.Trim());
        if (!string.IsNullOrWhiteSpace(row.Memo)) parts.Add(row.Memo!.Trim());
        if (parts.Count == 0)
        {
            parts.Add(kind == ImportSourceKind.BankStatement
                ? (row.Amount >= 0 ? options.DefaultIncomeNote : options.DefaultExpenseNote)
                : options.DefaultIncomeNote);
        }
        return string.Join(" / ", parts);
    }
}
