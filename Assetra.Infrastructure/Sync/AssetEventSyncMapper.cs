using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// <see cref="AssetEvent"/> ↔ <see cref="SyncEnvelope"/> mapper（v0.20.10）。
/// EntityType = "AssetEvent". Decimal 以 invariant string 序列化避免漂移；DateTime 以 ISO-8601 round-trip。
/// 父 asset_id 是必需欄位（子表）。
/// </summary>
public static class AssetEventSyncMapper
{
    public const string EntityType = "AssetEvent";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SyncEnvelope ToEnvelope(AssetEvent evt, EntityVersion version, bool isDeleted)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(version);

        if (isDeleted)
        {
            return new SyncEnvelope(evt.Id, EntityType, string.Empty, version, Deleted: true);
        }

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var dto = new EventPayloadDto(
            evt.Id,
            evt.AssetId,
            evt.EventType.ToString(),
            evt.EventDate.ToUniversalTime().ToString("o"),
            evt.Amount?.ToString(inv),
            evt.Quantity?.ToString(inv),
            evt.Note,
            evt.CashAccountId,
            evt.CreatedAt.ToUniversalTime().ToString("o"));

        return new SyncEnvelope(
            evt.Id, EntityType,
            JsonSerializer.Serialize(dto, Options),
            version, Deleted: false);
    }

    public static AssetEvent FromPayload(SyncEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.EntityType != EntityType)
            throw new ArgumentException($"Expected EntityType '{EntityType}', got '{envelope.EntityType}'.", nameof(envelope));
        if (envelope.Deleted)
            throw new InvalidOperationException("Cannot decode payload of a tombstone envelope.");

        var dto = JsonSerializer.Deserialize<EventPayloadDto>(envelope.PayloadJson, Options)
            ?? throw new InvalidOperationException("Empty AssetEvent payload.");

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return new AssetEvent(
            Id: dto.Id,
            AssetId: dto.AssetId,
            EventType: Enum.Parse<AssetEventType>(dto.EventType),
            EventDate: DateTime.Parse(dto.EventDate, inv, System.Globalization.DateTimeStyles.RoundtripKind),
            Amount: dto.Amount is null ? null : decimal.Parse(dto.Amount, inv),
            Quantity: dto.Quantity is null ? null : decimal.Parse(dto.Quantity, inv),
            Note: dto.Note,
            CashAccountId: dto.CashAccountId,
            CreatedAt: DateTime.Parse(dto.CreatedAt, inv, System.Globalization.DateTimeStyles.RoundtripKind));
    }

    private sealed record EventPayloadDto(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("asset_id")] Guid AssetId,
        [property: JsonPropertyName("event_type")] string EventType,
        [property: JsonPropertyName("event_date")] string EventDate,
        [property: JsonPropertyName("amount")] string? Amount,
        [property: JsonPropertyName("quantity")] string? Quantity,
        [property: JsonPropertyName("note")] string? Note,
        [property: JsonPropertyName("cash_account_id")] Guid? CashAccountId,
        [property: JsonPropertyName("created_at")] string CreatedAt);
}
