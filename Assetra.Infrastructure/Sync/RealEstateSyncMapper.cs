using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// <see cref="RealEstate"/> ↔ <see cref="SyncEnvelope"/> JSON mapper。
/// Decimal 以 string 序列化，避免 double round-trip 漂移。
/// </summary>
public static class RealEstateSyncMapper
{
    public const string EntityType = "RealEstate";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SyncEnvelope ToEnvelope(RealEstate entity, bool isDeleted)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (isDeleted)
            return new SyncEnvelope(entity.Id, EntityType, string.Empty, entity.Version, true);

        var inv = CultureInfo.InvariantCulture;
        var dto = new RealEstatePayloadDto(
            entity.Id,
            entity.Name,
            entity.Address,
            entity.PurchasePrice.ToString(inv),
            entity.PurchaseDate.ToString("yyyy-MM-dd"),
            entity.CurrentValue.ToString(inv),
            entity.MortgageBalance.ToString(inv),
            entity.Currency,
            entity.IsRental,
            entity.Status.ToString(),
            entity.Notes);

        return new SyncEnvelope(entity.Id, EntityType, JsonSerializer.Serialize(dto, Options), entity.Version, false);
    }

    public static RealEstate FromPayload(SyncEnvelope envelope, EntityVersion version)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.EntityType != EntityType)
            throw new ArgumentException($"Expected EntityType '{EntityType}', got '{envelope.EntityType}'.", nameof(envelope));
        if (envelope.Deleted)
            throw new InvalidOperationException("Cannot decode payload of a tombstone envelope.");

        var dto = JsonSerializer.Deserialize<RealEstatePayloadDto>(envelope.PayloadJson, Options)
            ?? throw new InvalidOperationException("Empty RealEstate payload.");

        var inv = CultureInfo.InvariantCulture;
        return new RealEstate(
            Id: dto.Id,
            Name: dto.Name,
            Address: dto.Address,
            PurchasePrice: decimal.Parse(dto.PurchasePrice, inv),
            PurchaseDate: DateOnly.Parse(dto.PurchaseDate),
            CurrentValue: decimal.Parse(dto.CurrentValue, inv),
            MortgageBalance: decimal.Parse(dto.MortgageBalance, inv),
            Currency: dto.Currency,
            IsRental: dto.IsRental,
            Status: Enum.Parse<RealEstateStatus>(dto.Status),
            Notes: dto.Notes,
            Version: version);
    }

    private sealed record RealEstatePayloadDto(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("address")] string Address,
        [property: JsonPropertyName("purchase_price")] string PurchasePrice,
        [property: JsonPropertyName("purchase_date")] string PurchaseDate,
        [property: JsonPropertyName("current_value")] string CurrentValue,
        [property: JsonPropertyName("mortgage_balance")] string MortgageBalance,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("is_rental")] bool IsRental,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("notes")] string? Notes);
}
