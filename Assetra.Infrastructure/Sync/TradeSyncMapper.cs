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
            trade.RecurringSourceId,
            // MultiCurrency-Trade-Refactor P1
            trade.InstrumentCurrency,
            trade.CommissionCurrency,
            trade.FxRate?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            trade.SettlementCurrency,
            trade.FxRateDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            trade.FxSource,
            // Portfolio-Groups-Refactor P1
            trade.PortfolioGroupId,
            // MultiCurrency-Reporting P4.5b — realized PnL market/FX split
            trade.RealizedMarketPnl?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            trade.RealizedFxPnl?.ToString(System.Globalization.CultureInfo.InvariantCulture));

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
            RecurringSourceId: dto.RecurringSourceId,
            // MultiCurrency-Trade-Refactor P1 — 舊 payload 未含這些 key 時走預設值
            // (InstrumentCurrency="TWD"、CommissionCurrency=null、FxRate=null)
            InstrumentCurrency: string.IsNullOrWhiteSpace(dto.InstrumentCurrency) ? "TWD" : dto.InstrumentCurrency!,
            CommissionCurrency: dto.CommissionCurrency,
            FxRate: dto.FxRate is null ? null : decimal.Parse(dto.FxRate, inv),
            SettlementCurrency: string.IsNullOrWhiteSpace(dto.SettlementCurrency) ? "TWD" : dto.SettlementCurrency!,
            FxRateDate: dto.FxRateDate is null ? null : DateOnly.Parse(dto.FxRateDate, inv),
            FxSource: dto.FxSource,
            // Portfolio-Groups-Refactor P1 — 舊 payload 缺欄位時走 null，repo 寫入時 fallback DefaultId
            PortfolioGroupId: dto.PortfolioGroupId,
            // MultiCurrency-Reporting P4.5b — 舊 payload 缺欄位走 null
            RealizedMarketPnl: dto.RealizedMarketPnl is null ? null : decimal.Parse(dto.RealizedMarketPnl, inv),
            RealizedFxPnl: dto.RealizedFxPnl is null ? null : decimal.Parse(dto.RealizedFxPnl, inv));
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
        [property: JsonPropertyName("recurring_source_id")] Guid? RecurringSourceId,
        // MultiCurrency-Trade-Refactor P1 — 新欄位附在尾端，舊 payload 缺欄位走預設
        [property: JsonPropertyName("instrument_currency")] string? InstrumentCurrency = null,
        [property: JsonPropertyName("commission_currency")] string? CommissionCurrency = null,
        [property: JsonPropertyName("fx_rate")] string? FxRate = null,
        [property: JsonPropertyName("settlement_currency")] string? SettlementCurrency = null,
        [property: JsonPropertyName("fx_rate_date")] string? FxRateDate = null,
        [property: JsonPropertyName("fx_source")] string? FxSource = null,
        // Portfolio-Groups-Refactor P1
        [property: JsonPropertyName("portfolio_group_id")] Guid? PortfolioGroupId = null,
        // MultiCurrency-Reporting P4.5b — realized PnL market/FX split (decimals as strings to avoid drift)
        [property: JsonPropertyName("realized_market_pnl")] string? RealizedMarketPnl = null,
        [property: JsonPropertyName("realized_fx_pnl")] string? RealizedFxPnl = null);
}
