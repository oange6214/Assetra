using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// <see cref="AssetGroup"/> ↔ <see cref="SyncEnvelope"/> mapper。
/// EntityType = "AssetGroup". snake_case + UnsafeRelaxedJsonEscaping。
/// </summary>
public static class AssetGroupSyncMapper
{
    public const string EntityType = "AssetGroup";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SyncEnvelope ToEnvelope(AssetGroup group, EntityVersion version, bool isDeleted)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(version);

        if (isDeleted)
        {
            return new SyncEnvelope(group.Id, EntityType, string.Empty, version, Deleted: true);
        }

        var dto = new GroupPayloadDto(
            group.Id,
            group.Name,
            group.Type.ToString(),
            group.Icon,
            group.SortOrder,
            group.IsSystem,
            group.CreatedDate.ToString("yyyy-MM-dd"));

        return new SyncEnvelope(
            group.Id, EntityType,
            JsonSerializer.Serialize(dto, Options),
            version, Deleted: false);
    }

    public static AssetGroup FromPayload(SyncEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.EntityType != EntityType)
            throw new ArgumentException($"Expected EntityType '{EntityType}', got '{envelope.EntityType}'.", nameof(envelope));
        if (envelope.Deleted)
            throw new InvalidOperationException("Cannot decode payload of a tombstone envelope.");

        var dto = JsonSerializer.Deserialize<GroupPayloadDto>(envelope.PayloadJson, Options)
            ?? throw new InvalidOperationException("Empty AssetGroup payload.");

        return new AssetGroup(
            Id: dto.Id,
            Name: dto.Name,
            Type: Enum.Parse<FinancialType>(dto.Type),
            Icon: dto.Icon,
            SortOrder: dto.SortOrder,
            IsSystem: dto.IsSystem,
            CreatedDate: DateOnly.Parse(dto.CreatedDate));
    }

    private sealed record GroupPayloadDto(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("financial_type")] string Type,
        [property: JsonPropertyName("icon")] string? Icon,
        [property: JsonPropertyName("sort_order")] int SortOrder,
        [property: JsonPropertyName("is_system")] bool IsSystem,
        [property: JsonPropertyName("created_date")] string CreatedDate);
}
