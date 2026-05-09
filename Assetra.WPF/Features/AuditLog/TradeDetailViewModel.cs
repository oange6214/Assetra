using System.Collections.ObjectModel;
using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.AuditLog;

/// <summary>
/// View-model for the right-pane <c>TradeDetailCardView</c>. Wraps a parsed
/// <see cref="Trade"/> snapshot and exposes a flat list of label/value rows
/// curated to the trade's <see cref="TradeType"/> (so a Deposit row doesn't
/// show empty Symbol / Quantity fields).
///
/// <para>
/// When constructed with a <c>previous</c> snapshot the rows include diff
/// markers — used by the AuditLog UI to highlight what changed between an
/// edit-replace pair.
/// </para>
/// </summary>
public sealed partial class TradeDetailViewModel : ObservableObject
{
    public Trade? Snapshot { get; }
    public Trade? Previous { get; }
    public string RawJson { get; }

    public bool HasSnapshot => Snapshot is not null;
    public bool HasDiff => Previous is not null && Snapshot is not null;

    public string HeaderLine { get; }
    public string SubHeaderLine { get; }
    public string TypeLabel { get; }

    public ObservableCollection<TradeFieldRow> Fields { get; } = new();

    public TradeDetailViewModel(string rawJson, Trade? snapshot, Trade? previous = null)
    {
        RawJson = PrettyPrintJson(rawJson);
        Snapshot = snapshot;
        Previous = previous;

        if (snapshot is null)
        {
            HeaderLine = "(無法解析快照)";
            SubHeaderLine = string.Empty;
            TypeLabel = string.Empty;
            return;
        }

        // Header: 「0056 元大高股息ETF (TWSE)」
        var name = string.IsNullOrWhiteSpace(snapshot.Name) ? snapshot.Symbol : snapshot.Name;
        HeaderLine = string.IsNullOrWhiteSpace(snapshot.Symbol)
            ? snapshot.Type.ToString()
            : $"{snapshot.Symbol}  {name}";
        SubHeaderLine = string.IsNullOrWhiteSpace(snapshot.Exchange) ? string.Empty : snapshot.Exchange;
        TypeLabel = snapshot.Type.ToString();

        BuildFields(snapshot, previous);
    }

    private void BuildFields(Trade t, Trade? prev)
    {
        // Common fields shown for every type.
        Add("交易日期", t.TradeDate.ToString("yyyy-MM-dd"), prev?.TradeDate.ToString("yyyy-MM-dd"));

        // Type-specific fields (avoid spamming irrelevant nulls).
        switch (t.Type)
        {
            case TradeType.Buy:
            case TradeType.Sell:
                Add("數量", $"{t.Quantity:N0}", prev?.Quantity.ToString("N0"));
                Add("價格", $"{t.Price:N4}", prev?.Price.ToString("N4"));
                Add("總額", $"{(t.Price * t.Quantity):N2}",
                    prev is null ? null : $"{(prev.Price * prev.Quantity):N2}");
                if (t.Type == TradeType.Sell)
                {
                    AddIfPresent("已實現損益", t.RealizedPnl?.ToString("N2"), prev?.RealizedPnl?.ToString("N2"));
                    AddIfPresent("損益率", t.RealizedPnlPct is null ? null : $"{t.RealizedPnlPct:P2}",
                        prev?.RealizedPnlPct is null ? null : $"{prev.RealizedPnlPct:P2}");
                }
                AddIfPresent("手續費", t.Commission?.ToString("N0"), prev?.Commission?.ToString("N0"));
                AddIfPresent("折扣", t.CommissionDiscount?.ToString("P0"), prev?.CommissionDiscount?.ToString("P0"));
                break;

            case TradeType.CashDividend:
                Add("股利/股", $"{t.Price:N4}", prev?.Price.ToString("N4"));
                Add("除息日持股", $"{t.Quantity:N0}", prev?.Quantity.ToString("N0"));
                Add("入帳金額", $"{t.CashAmount:N0}", prev?.CashAmount?.ToString("N0"));
                break;

            case TradeType.StockDividend:
                Add("配得股數", $"{t.Quantity:N0}", prev?.Quantity.ToString("N0"));
                break;

            case TradeType.Income:
            case TradeType.Deposit:
            case TradeType.Withdrawal:
                Add("金額", $"{t.CashAmount:N0}", prev?.CashAmount?.ToString("N0"));
                break;

            case TradeType.Transfer:
                Add("金額", $"{t.CashAmount:N0}", prev?.CashAmount?.ToString("N0"));
                Add("來源帳戶", ShortGuid(t.CashAccountId), ShortGuid(prev?.CashAccountId));
                Add("目標帳戶", ShortGuid(t.ToCashAccountId), ShortGuid(prev?.ToCashAccountId));
                break;

            case TradeType.LoanBorrow:
                Add("貸款", t.LoanLabel ?? "—", prev?.LoanLabel);
                Add("撥款金額", $"{t.CashAmount:N0}", prev?.CashAmount?.ToString("N0"));
                break;

            case TradeType.LoanRepay:
                Add("貸款", t.LoanLabel ?? "—", prev?.LoanLabel);
                Add("付款總額", $"{t.CashAmount:N0}", prev?.CashAmount?.ToString("N0"));
                AddIfPresent("本金", t.Principal?.ToString("N0"), prev?.Principal?.ToString("N0"));
                AddIfPresent("利息", t.InterestPaid?.ToString("N0"), prev?.InterestPaid?.ToString("N0"));
                break;

            case TradeType.CreditCardCharge:
            case TradeType.CreditCardPayment:
                Add("金額", $"{t.CashAmount:N0}", prev?.CashAmount?.ToString("N0"));
                Add("信用卡", ShortGuid(t.LiabilityAssetId), ShortGuid(prev?.LiabilityAssetId));
                break;
        }

        // Cross-cutting fields.
        AddIfPresent("現金帳戶", ShortGuid(t.CashAccountId), ShortGuid(prev?.CashAccountId));
        AddIfPresent("持倉批次", ShortGuid(t.PortfolioEntryId), ShortGuid(prev?.PortfolioEntryId));
        AddIfPresent("分類", ShortGuid(t.CategoryId), ShortGuid(prev?.CategoryId));
        AddIfPresent("週期來源", ShortGuid(t.RecurringSourceId), ShortGuid(prev?.RecurringSourceId));
        AddIfPresent("父交易", ShortGuid(t.ParentTradeId), ShortGuid(prev?.ParentTradeId));
        AddIfPresent("備註", t.Note, prev?.Note);

        // Always show Trade Id last (for cross-reference).
        Add("Trade Id", t.Id.ToString(), prev?.Id.ToString());
    }

    private void Add(string label, string value, string? previousValue)
    {
        var changed = previousValue is not null && !string.Equals(previousValue, value, StringComparison.Ordinal);
        Fields.Add(new TradeFieldRow(label, value ?? string.Empty, previousValue, changed));
    }

    private void AddIfPresent(string label, string? value, string? previousValue)
    {
        var hasCurrent = !string.IsNullOrWhiteSpace(value) && value != "—";
        var hasPrev = !string.IsNullOrWhiteSpace(previousValue) && previousValue != "—";
        if (!hasCurrent && !hasPrev) return;
        Add(label, value ?? "—", previousValue);
    }

    private static string ShortGuid(Guid? g) => g is null || g == Guid.Empty ? "—" : g.Value.ToString()[..8] + "…";

    /// <summary>Pretty-prints raw JSON with 2-space indent for the Raw view.</summary>
    private static string PrettyPrintJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            return System.Text.Json.JsonSerializer.Serialize(doc.RootElement,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return raw;
        }
    }
}

/// <summary>One row in the detail card's field table.</summary>
public sealed record TradeFieldRow(string Label, string Value, string? PreviousValue, bool IsChanged)
{
    public bool HasPrevious => !string.IsNullOrWhiteSpace(PreviousValue);
}
