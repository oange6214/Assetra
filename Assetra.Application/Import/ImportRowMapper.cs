using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;

namespace Assetra.Application.Import;

/// <summary>
/// 預設 mapper：依 <see cref="ImportSourceKind"/> 將 <see cref="ImportPreviewRow"/> 轉成 <see cref="Trade"/>。
/// 行為與 v0.7 內嵌於 <see cref="ImportApplyService"/> 的私有 helper 一致；
/// v0.8 新增可選 <see cref="IImportRuleEngine"/>：若注入且命中規則，會把 <see cref="Trade.CategoryId"/> 帶入。
/// </summary>
public sealed class ImportRowMapper : IImportRowMapper
{
    private readonly IImportRuleEngine? _ruleEngine;

    public ImportRowMapper()
    {
    }

    public ImportRowMapper(IImportRuleEngine? ruleEngine)
    {
        _ruleEngine = ruleEngine;
    }

    public Trade? Map(ImportPreviewRow row, ImportSourceKind kind, ImportApplyOptions options, IList<string> warnings)
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

        if (trade is null) return null;
        if (_ruleEngine is not null
            && _ruleEngine.TryResolveCategory(row, out var categoryId)
            && categoryId is { } cid)
        {
            trade = trade with { CategoryId = cid };
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
