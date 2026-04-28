using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// <see cref="PortfolioEntry"/> ↔ <see cref="SyncEnvelope"/> 的 JSON mapper，鏡
/// <see cref="AssetSyncMapper"/>。snake_case + UnsafeRelaxedJsonEscaping。
/// Tombstone 的 PayloadJson 為空字串。
/// </summary>
public static class PortfolioSyncMapper
{
    public const string EntityType = "Portfolio";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SyncEnvelope ToEnvelope(PortfolioEntry entry, EntityVersion version, bool isDeleted)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(version);

        if (isDeleted)
        {
            return new SyncEnvelope(
                EntityId: entry.Id,
                EntityType: EntityType,
                PayloadJson: string.Empty,
                Version: version,
                Deleted: true);
        }

        var dto = new PortfolioPayloadDto(
            entry.Id,
            entry.Symbol,
            entry.Exchange,
            entry.AssetType.ToString(),
            entry.DisplayName,
            entry.Currency,
            entry.IsActive,
            entry.IsEtf);

        return new SyncEnvelope(
            EntityId: entry.Id,
            EntityType: EntityType,
            PayloadJson: JsonSerializer.Serialize(dto, Options),
            Version: version,
            Deleted: false);
    }

    public static PortfolioEntry FromPayload(SyncEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.EntityType != EntityType)
            throw new ArgumentException($"Expected EntityType '{EntityType}', got '{envelope.EntityType}'.", nameof(envelope));
        if (envelope.Deleted)
            throw new InvalidOperationException("Cannot decode payload of a tombstone envelope.");

        var dto = JsonSerializer.Deserialize<PortfolioPayloadDto>(envelope.PayloadJson, Options)
            ?? throw new InvalidOperationException("Empty Portfolio payload.");

        return new PortfolioEntry(
            Id: dto.Id,
            Symbol: dto.Symbol,
            Exchange: dto.Exchange,
            AssetType: Enum.Parse<AssetType>(dto.AssetType),
            DisplayName: dto.DisplayName,
            Currency: dto.Currency,
            IsActive: dto.IsActive,
            IsEtf: dto.IsEtf);
    }

    private sealed record PortfolioPayloadDto(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("symbol")] string Symbol,
        [property: JsonPropertyName("exchange")] string Exchange,
        [property: JsonPropertyName("asset_type")] string AssetType,
        [property: JsonPropertyName("display_name")] string DisplayName,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("is_etf")] bool IsEtf);
}
