using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// <see cref="PortfolioGroup"/> ↔ <see cref="SyncEnvelope"/> JSON mapper.
/// </summary>
public static class PortfolioGroupSyncMapper
{
    public const string EntityType = "PortfolioGroup";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SyncEnvelope ToEnvelope(PortfolioGroup group, EntityVersion version, bool isDeleted)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(version);

        if (isDeleted)
        {
            return new SyncEnvelope(
                EntityId: group.Id,
                EntityType: EntityType,
                PayloadJson: string.Empty,
                Version: version,
                Deleted: true);
        }

        var dto = new GroupPayloadDto(
            group.Id,
            group.Name,
            group.ColorHex,
            group.Description,
            group.IconKey,
            group.SortOrder,
            group.DefaultCashAccountId,
            group.IsSystem);

        return new SyncEnvelope(
            EntityId: group.Id,
            EntityType: EntityType,
            PayloadJson: JsonSerializer.Serialize(dto, Options),
            Version: version,
            Deleted: false);
    }

    public static PortfolioGroup FromPayload(SyncEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.EntityType != EntityType)
            throw new ArgumentException($"Expected EntityType '{EntityType}', got '{envelope.EntityType}'.", nameof(envelope));
        if (envelope.Deleted)
            throw new InvalidOperationException("Cannot decode payload of a tombstone envelope.");

        var dto = JsonSerializer.Deserialize<GroupPayloadDto>(envelope.PayloadJson, Options)
            ?? throw new InvalidOperationException("Empty PortfolioGroup payload.");

        return new PortfolioGroup(
            Id: dto.Id,
            Name: dto.Name,
            ColorHex: dto.ColorHex,
            Description: dto.Description,
            IconKey: dto.IconKey,
            SortOrder: dto.SortOrder,
            DefaultCashAccountId: dto.DefaultCashAccountId,
            IsSystem: dto.IsSystem);
    }

    private sealed record GroupPayloadDto(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("color_hex")] string? ColorHex,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("icon_key")] string? IconKey,
        [property: JsonPropertyName("sort_order")] int SortOrder,
        [property: JsonPropertyName("default_cash_account_id")] Guid? DefaultCashAccountId,
        [property: JsonPropertyName("is_system")] bool IsSystem);
}
