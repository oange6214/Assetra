using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// <see cref="AlertRule"/> ↔ <see cref="SyncEnvelope"/> JSON mapper。
/// Decimal 以 string 序列化避免 double round-trip 漂移；trigger_time 用 ISO 8601。
/// </summary>
public static class AlertSyncMapper
{
    public const string EntityType = "Alert";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SyncEnvelope ToEnvelope(AlertRule entity, bool isDeleted)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (isDeleted)
            return new SyncEnvelope(entity.Id, EntityType, string.Empty, entity.Version, true);

        var inv = CultureInfo.InvariantCulture;
        var dto = new AlertPayloadDto(
            entity.Id,
            entity.Symbol,
            entity.Exchange,
            entity.Condition.ToString(),
            entity.TargetPrice.ToString(inv),
            entity.IsTriggered,
            entity.TriggerTime?.ToString("o"));

        return new SyncEnvelope(entity.Id, EntityType, JsonSerializer.Serialize(dto, Options), entity.Version, false);
    }

    public static AlertRule FromPayload(SyncEnvelope envelope, EntityVersion version)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.EntityType != EntityType)
            throw new ArgumentException($"Expected EntityType '{EntityType}', got '{envelope.EntityType}'.", nameof(envelope));
        if (envelope.Deleted)
            throw new InvalidOperationException("Cannot decode payload of a tombstone envelope.");

        var dto = JsonSerializer.Deserialize<AlertPayloadDto>(envelope.PayloadJson, Options)
            ?? throw new InvalidOperationException("Empty Alert payload.");

        var inv = CultureInfo.InvariantCulture;
        return new AlertRule(
            Id: dto.Id,
            Symbol: dto.Symbol,
            Exchange: dto.Exchange,
            Condition: Enum.Parse<AlertCondition>(dto.Condition),
            TargetPrice: decimal.Parse(dto.TargetPrice, inv),
            IsTriggered: dto.IsTriggered,
            TriggerTime: string.IsNullOrEmpty(dto.TriggerTime) ? null : DateTimeOffset.Parse(dto.TriggerTime))
        {
            Version = version,
        };
    }

    private sealed record AlertPayloadDto(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("symbol")] string Symbol,
        [property: JsonPropertyName("exchange")] string Exchange,
        [property: JsonPropertyName("condition")] string Condition,
        [property: JsonPropertyName("target_price")] string TargetPrice,
        [property: JsonPropertyName("is_triggered")] bool IsTriggered,
        [property: JsonPropertyName("trigger_time")] string? TriggerTime);
}
