using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// <see cref="PhysicalAsset"/> ↔ <see cref="SyncEnvelope"/> JSON mapper。
/// Decimal 以 string 序列化，避免 double round-trip 漂移。
/// </summary>
public static class PhysicalAssetSyncMapper
{
    public const string EntityType = "PhysicalAsset";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SyncEnvelope ToEnvelope(PhysicalAsset entity, bool isDeleted)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (isDeleted)
            return new SyncEnvelope(entity.Id, EntityType, string.Empty, entity.Version, true);

        var inv = CultureInfo.InvariantCulture;
        var dto = new PhysicalAssetPayloadDto(
            entity.Id,
            entity.Name,
            entity.Category.ToString(),
            entity.Description,
            entity.AcquisitionCost.ToString(inv),
            entity.AcquisitionDate.ToString("yyyy-MM-dd"),
            entity.CurrentValue.ToString(inv),
            entity.ValuationMethod,
            entity.Currency,
            entity.Status.ToString(),
            entity.Notes);

        return new SyncEnvelope(entity.Id, EntityType, JsonSerializer.Serialize(dto, Options), entity.Version, false);
    }

    public static PhysicalAsset FromPayload(SyncEnvelope envelope, EntityVersion version)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.EntityType != EntityType)
            throw new ArgumentException($"Expected EntityType '{EntityType}', got '{envelope.EntityType}'.", nameof(envelope));
        if (envelope.Deleted)
            throw new InvalidOperationException("Cannot decode payload of a tombstone envelope.");

        var dto = JsonSerializer.Deserialize<PhysicalAssetPayloadDto>(envelope.PayloadJson, Options)
            ?? throw new InvalidOperationException("Empty PhysicalAsset payload.");

        var inv = CultureInfo.InvariantCulture;
        return new PhysicalAsset(
            Id: dto.Id,
            Name: dto.Name,
            Category: Enum.Parse<PhysicalAssetCategory>(dto.Category),
            Description: dto.Description,
            AcquisitionCost: decimal.Parse(dto.AcquisitionCost, inv),
            AcquisitionDate: DateOnly.Parse(dto.AcquisitionDate),
            CurrentValue: decimal.Parse(dto.CurrentValue, inv),
            ValuationMethod: dto.ValuationMethod,
            Currency: dto.Currency,
            Status: Enum.Parse<PhysicalAssetStatus>(dto.Status),
            Notes: dto.Notes,
            Version: version);
    }

    private sealed record PhysicalAssetPayloadDto(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("acquisition_cost")] string AcquisitionCost,
        [property: JsonPropertyName("acquisition_date")] string AcquisitionDate,
        [property: JsonPropertyName("current_value")] string CurrentValue,
        [property: JsonPropertyName("valuation_method")] string ValuationMethod,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("notes")] string? Notes);
}
