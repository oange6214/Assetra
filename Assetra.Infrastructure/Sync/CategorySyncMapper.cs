using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// <see cref="ExpenseCategory"/> ↔ <see cref="SyncEnvelope"/> 的 JSON mapper。
/// <para>
/// PayloadJson 採用 snake_case + 顯式 <see cref="JsonPropertyName"/>，與 wire protocol 風格一致；
/// <c>id</c> 同時放在 envelope 的 <see cref="SyncEnvelope.EntityId"/> 與 payload 內，方便除錯
/// 而成本可忽略。Tombstone 的 envelope <c>PayloadJson</c> 為空字串。
/// </para>
/// </summary>
public static class CategorySyncMapper
{
    public const string EntityType = "Category";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // CJK / 全形符號保留原字（避免 \uXXXX 膨脹）。已加密層會包進 base64，毋需 HTML escape。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SyncEnvelope ToEnvelope(ExpenseCategory category, EntityVersion version, bool isDeleted)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(version);

        if (isDeleted)
        {
            return new SyncEnvelope(
                EntityId: category.Id,
                EntityType: EntityType,
                PayloadJson: string.Empty,
                Version: version,
                Deleted: true);
        }

        var dto = new CategoryPayloadDto(
            category.Id,
            category.Name,
            category.Kind.ToString(),
            category.ParentId,
            category.Icon,
            category.ColorHex,
            category.SortOrder,
            category.IsArchived);

        return new SyncEnvelope(
            EntityId: category.Id,
            EntityType: EntityType,
            PayloadJson: JsonSerializer.Serialize(dto, Options),
            Version: version,
            Deleted: false);
    }

    public static ExpenseCategory FromPayload(SyncEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.EntityType != EntityType)
            throw new ArgumentException($"Expected EntityType '{EntityType}', got '{envelope.EntityType}'.", nameof(envelope));
        if (envelope.Deleted)
            throw new InvalidOperationException("Cannot decode payload of a tombstone envelope.");

        var dto = JsonSerializer.Deserialize<CategoryPayloadDto>(envelope.PayloadJson, Options)
            ?? throw new InvalidOperationException("Empty Category payload.");

        return new ExpenseCategory(
            Id: dto.Id,
            Name: dto.Name,
            Kind: Enum.Parse<CategoryKind>(dto.Kind),
            ParentId: dto.ParentId,
            Icon: dto.Icon,
            ColorHex: dto.ColorHex,
            SortOrder: dto.SortOrder,
            IsArchived: dto.IsArchived);
    }

    private sealed record CategoryPayloadDto(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("parent_id")] Guid? ParentId,
        [property: JsonPropertyName("icon")] string? Icon,
        [property: JsonPropertyName("color_hex")] string? ColorHex,
        [property: JsonPropertyName("sort_order")] int SortOrder,
        [property: JsonPropertyName("is_archived")] bool IsArchived);
}
