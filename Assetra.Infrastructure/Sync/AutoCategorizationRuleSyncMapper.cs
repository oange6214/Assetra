using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// <see cref="AutoCategorizationRule"/> ↔ <see cref="SyncEnvelope"/> mapper（v0.20.11）。
/// EntityType = "AutoCategorizationRule". snake_case + UnsafeRelaxedJsonEscaping。
/// Enum 以 int 序列化（與 DB 表示一致），避免 wire format 受名稱重命名影響。
/// </summary>
public static class AutoCategorizationRuleSyncMapper
{
    public const string EntityType = "AutoCategorizationRule";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SyncEnvelope ToEnvelope(AutoCategorizationRule rule, EntityVersion version, bool isDeleted)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(version);

        if (isDeleted)
        {
            return new SyncEnvelope(rule.Id, EntityType, string.Empty, version, Deleted: true);
        }

        var dto = new RulePayloadDto(
            rule.Id,
            rule.KeywordPattern,
            rule.CategoryId,
            rule.Priority,
            rule.IsEnabled,
            rule.MatchCaseSensitive,
            rule.Name,
            (int)rule.MatchField,
            (int)rule.MatchType,
            (int)rule.AppliesTo);

        return new SyncEnvelope(
            rule.Id, EntityType,
            JsonSerializer.Serialize(dto, Options),
            version, Deleted: false);
    }

    public static AutoCategorizationRule FromPayload(SyncEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.EntityType != EntityType)
            throw new ArgumentException($"Expected EntityType '{EntityType}', got '{envelope.EntityType}'.", nameof(envelope));
        if (envelope.Deleted)
            throw new InvalidOperationException("Cannot decode payload of a tombstone envelope.");

        var dto = JsonSerializer.Deserialize<RulePayloadDto>(envelope.PayloadJson, Options)
            ?? throw new InvalidOperationException("Empty AutoCategorizationRule payload.");

        return new AutoCategorizationRule(
            Id: dto.Id,
            KeywordPattern: dto.KeywordPattern,
            CategoryId: dto.CategoryId,
            Priority: dto.Priority,
            IsEnabled: dto.IsEnabled,
            MatchCaseSensitive: dto.MatchCaseSensitive,
            Name: dto.Name,
            MatchField: (AutoCategorizationMatchField)dto.MatchField,
            MatchType: (AutoCategorizationMatchType)dto.MatchType,
            AppliesTo: (AutoCategorizationScope)dto.AppliesTo);
    }

    private sealed record RulePayloadDto(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("keyword_pattern")] string KeywordPattern,
        [property: JsonPropertyName("category_id")] Guid CategoryId,
        [property: JsonPropertyName("priority")] int Priority,
        [property: JsonPropertyName("is_enabled")] bool IsEnabled,
        [property: JsonPropertyName("match_case_sensitive")] bool MatchCaseSensitive,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("match_field")] int MatchField,
        [property: JsonPropertyName("match_type")] int MatchType,
        [property: JsonPropertyName("applies_to")] int AppliesTo);
}
