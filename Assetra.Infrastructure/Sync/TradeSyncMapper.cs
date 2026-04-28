using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// <see cref="Trade"/> ↔ <see cref="SyncEnvelope"/> 的 JSON mapper，鏡 <see cref="CategorySyncMapper"/>。
/// snake_case + UnsafeRelaxedJsonEscaping（CJK 不轉義）。Tombstone 的 PayloadJson 為空字串。
/// 為避免 decimal/double round-trip 漂移，金額欄位以 string 序列化（invariant culture）。
/// </summary>
public static class TradeSyncMapper
{
    public const string EntityType = "Trade";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SyncEnvelope ToEnvelope(Trade trade, EntityVersion version, bool isDeleted)
    {
        ArgumentNullException.ThrowIfNull(trade);
        ArgumentNullException.ThrowIfNull(version);

        if (isDeleted)
        {
            return new SyncEnvelope(
                EntityId: trade.Id,
                EntityType: EntityType,
                PayloadJson: string.Empty,
                Version: version,
                Deleted: true);
        }

        var dto = new TradePayloadDto(
            trade.Id,
            trade.Symbol,
            trade.Exchange,
            trade.Name,
            trade.Type.ToString(),
            trade.TradeDate.ToUniversalTime().ToString("o"),
            trade.Price.ToString(System.Globalization.CultureInfo.InvariantCulture),
            trade.Quantity,
            trade.RealizedPnl?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            trade.RealizedPnlPct?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            trade.CashAmount?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            trade.CashAccountId,
            trade.Note,
            trade.PortfolioEntryId,
            trade.Commission?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            trade.CommissionDiscount?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            trade.LoanLabel,
            trade.Principal?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            trade.InterestPaid?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            trade.ToCashAccountId,
            trade.LiabilityAssetId,
            trade.ParentTradeId,
            trade.CategoryId,
            trade.RecurringSourceId);

        return new SyncEnvelope(
            EntityId: trade.Id,
            EntityType: EntityType,
            PayloadJson: JsonSerializer.Serialize(dto, Options),
            Version: version,
            Deleted: false);
    }

    public static Trade FromPayload(SyncEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.EntityType != EntityType)
            throw new ArgumentException($"Expected EntityType '{EntityType}', got '{envelope.EntityType}'.", nameof(envelope));
        if (envelope.Deleted)
            throw new InvalidOperationException("Cannot decode payload of a tombstone envelope.");

        var dto = JsonSerializer.Deserialize<TradePayloadDto>(envelope.PayloadJson, Options)
            ?? throw new InvalidOperationException("Empty Trade payload.");

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return new Trade(
            Id: dto.Id,
            Symbol: dto.Symbol,
            Exchange: dto.Exchange,
            Name: dto.Name,
            Type: Enum.Parse<TradeType>(dto.Type),
            TradeDate: DateTime.Parse(dto.TradeDate, inv, System.Globalization.DateTimeStyles.RoundtripKind),
            Price: decimal.Parse(dto.Price, inv),
            Quantity: dto.Quantity,
            RealizedPnl: dto.RealizedPnl is null ? null : decimal.Parse(dto.RealizedPnl, inv),
            RealizedPnlPct: dto.RealizedPnlPct is null ? null : decimal.Parse(dto.RealizedPnlPct, inv),
            CashAmount: dto.CashAmount is null ? null : decimal.Parse(dto.CashAmount, inv),
            CashAccountId: dto.CashAccountId,
            Note: dto.Note,
            PortfolioEntryId: dto.PortfolioEntryId,
            Commission: dto.Commission is null ? null : decimal.Parse(dto.Commission, inv),
            CommissionDiscount: dto.CommissionDiscount is null ? null : decimal.Parse(dto.CommissionDiscount, inv),
            LoanLabel: dto.LoanLabel,
            Principal: dto.Principal is null ? null : decimal.Parse(dto.Principal, inv),
            InterestPaid: dto.InterestPaid is null ? null : decimal.Parse(dto.InterestPaid, inv),
            ToCashAccountId: dto.ToCashAccountId,
            LiabilityAssetId: dto.LiabilityAssetId,
            ParentTradeId: dto.ParentTradeId,
            CategoryId: dto.CategoryId,
            RecurringSourceId: dto.RecurringSourceId);
    }

    private sealed record TradePayloadDto(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("symbol")] string Symbol,
        [property: JsonPropertyName("exchange")] string Exchange,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("trade_type")] string Type,
        [property: JsonPropertyName("trade_date")] string TradeDate,
        [property: JsonPropertyName("price")] string Price,
        [property: JsonPropertyName("quantity")] int Quantity,
        [property: JsonPropertyName("realized_pnl")] string? RealizedPnl,
        [property: JsonPropertyName("realized_pnl_pct")] string? RealizedPnlPct,
        [property: JsonPropertyName("cash_amount")] string? CashAmount,
        [property: JsonPropertyName("cash_account_id")] Guid? CashAccountId,
        [property: JsonPropertyName("note")] string? Note,
        [property: JsonPropertyName("portfolio_entry_id")] Guid? PortfolioEntryId,
        [property: JsonPropertyName("commission")] string? Commission,
        [property: JsonPropertyName("commission_discount")] string? CommissionDiscount,
        [property: JsonPropertyName("loan_label")] string? LoanLabel,
        [property: JsonPropertyName("principal")] string? Principal,
        [property: JsonPropertyName("interest_paid")] string? InterestPaid,
        [property: JsonPropertyName("to_cash_account_id")] Guid? ToCashAccountId,
        [property: JsonPropertyName("liability_asset_id")] Guid? LiabilityAssetId,
        [property: JsonPropertyName("parent_trade_id")] Guid? ParentTradeId,
        [property: JsonPropertyName("category_id")] Guid? CategoryId,
        [property: JsonPropertyName("recurring_source_id")] Guid? RecurringSourceId);
}
