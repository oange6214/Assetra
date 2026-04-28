using System.Text.Json.Serialization;

namespace Assetra.Infrastructure.Sync.Http;

/// <summary>
/// HTTP 線路層 DTO。命名統一 snake_case 以配合大多數 serverless backend（Cloudflare Workers / Supabase）的慣例；
/// 內部以 <c>JsonPropertyName</c> 顯式映射，避免 .NET property naming 影響 wire format。
/// <para>
/// 本檔內所有型別僅用於 (de)serialization，不對外暴露——上層只看 <see cref="Assetra.Core.Models.Sync.SyncEnvelope"/> 等 domain types。
/// </para>
/// </summary>
internal sealed record EntityVersionWireDto
{
    [JsonPropertyName("version")]
    public long Version { get; init; }

    [JsonPropertyName("last_modified_at")]
    public DateTimeOffset LastModifiedAt { get; init; }

    [JsonPropertyName("last_modified_by_device")]
    public string LastModifiedByDevice { get; init; } = string.Empty;
}

internal sealed record EnvelopeWireDto
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; init; }

    [JsonPropertyName("entity_type")]
    public string EntityType { get; init; } = string.Empty;

    [JsonPropertyName("payload")]
    public string PayloadJson { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public EntityVersionWireDto Version { get; init; } = new();

    [JsonPropertyName("deleted")]
    public bool Deleted { get; init; }
}

internal sealed record PullResponseDto
{
    [JsonPropertyName("envelopes")]
    public List<EnvelopeWireDto> Envelopes { get; init; } = new();

    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; init; }
}

internal sealed record PushRequestDto
{
    [JsonPropertyName("device_id")]
    public string DeviceId { get; init; } = string.Empty;

    [JsonPropertyName("envelopes")]
    public List<EnvelopeWireDto> Envelopes { get; init; } = new();
}

internal sealed record ConflictWireDto
{
    [JsonPropertyName("local")]
    public EnvelopeWireDto Local { get; init; } = new();

    [JsonPropertyName("remote")]
    public EnvelopeWireDto Remote { get; init; } = new();
}

internal sealed record PushResponseDto
{
    [JsonPropertyName("accepted")]
    public List<Guid> Accepted { get; init; } = new();

    [JsonPropertyName("conflicts")]
    public List<ConflictWireDto> Conflicts { get; init; } = new();

    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; init; }
}
