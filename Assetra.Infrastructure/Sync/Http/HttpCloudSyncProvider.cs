using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync.Http;

/// <summary>
/// 透過 HTTP 與雲端後端對話的 <see cref="ICloudSyncProvider"/>。
/// 後端可以是 Cloudflare Workers / Supabase Edge Functions / 自架 ASP.NET，只要實作 wire protocol（見 docs/architecture/sync-wire-protocol.md）。
/// <para>
/// **線路：**
/// <list type="bullet">
///   <item>Pull → <c>GET {baseUrl}/sync/pull?cursor={cursor}</c></item>
///   <item>Push → <c>POST {baseUrl}/sync/push</c>，body = <see cref="PushRequestDto"/></item>
/// </list>
/// 失敗（4xx / 5xx）會 <c>throw HttpRequestException</c>，由上層決定 retry / 顯示給使用者。
/// </para>
/// <para>
/// 本層**完全不知道加密**——caller 應在外層用 <see cref="EncryptingCloudSyncProvider"/> 包住，
/// 因此 wire 上看到的 <c>payload</c> 已是密文 base64 字串。
/// </para>
/// </summary>
public sealed class HttpCloudSyncProvider : ICloudSyncProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
    };

    private readonly HttpClient _http;
    private readonly SyncEndpointOptions _options;

    public HttpCloudSyncProvider(HttpClient http, SyncEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
            throw new ArgumentException("BaseUrl is required.", nameof(options));

        _http = http;
        _options = options;
    }

    public async Task<SyncPullResult> PullAsync(SyncMetadata metadata, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var url = string.IsNullOrEmpty(metadata.Cursor)
            ? $"{_options.BaseUrl}/sync/pull"
            : $"{_options.BaseUrl}/sync/pull?cursor={Uri.EscapeDataString(metadata.Cursor)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuth(request);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content
            .ReadFromJsonAsync<PullResponseDto>(JsonOptions, ct)
            .ConfigureAwait(false)
            ?? throw new HttpRequestException("Empty pull response body.");

        var envelopes = dto.Envelopes.Select(WireToDomain).ToList();
        return new SyncPullResult(envelopes, dto.NextCursor);
    }

    public async Task<SyncPushResult> PushAsync(
        SyncMetadata metadata,
        IReadOnlyList<SyncEnvelope> envelopes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(envelopes);

        var body = new PushRequestDto
        {
            DeviceId = metadata.DeviceId ?? string.Empty,
            Envelopes = envelopes.Select(DomainToWire).ToList(),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/sync/push")
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        AddAuth(request);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var dto = await response.Content
            .ReadFromJsonAsync<PushResponseDto>(JsonOptions, ct)
            .ConfigureAwait(false)
            ?? throw new HttpRequestException("Empty push response body.");

        var conflicts = dto.Conflicts
            .Select(c => new SyncConflict(WireToDomain(c.Local), WireToDomain(c.Remote)))
            .ToList();

        return new SyncPushResult(dto.Accepted, conflicts, dto.NextCursor);
    }

    private void AddAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(_options.AuthToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AuthToken);
    }

    internal static EnvelopeWireDto DomainToWire(SyncEnvelope env) => new()
    {
        EntityId = env.EntityId,
        EntityType = env.EntityType,
        PayloadJson = env.PayloadJson,
        Deleted = env.Deleted,
        Version = new EntityVersionWireDto
        {
            Version = env.Version.Version,
            LastModifiedAt = env.Version.LastModifiedAt,
            LastModifiedByDevice = env.Version.LastModifiedByDevice,
        },
    };

    internal static SyncEnvelope WireToDomain(EnvelopeWireDto dto) => new(
        EntityId: dto.EntityId,
        EntityType: dto.EntityType,
        PayloadJson: dto.PayloadJson,
        Version: new EntityVersion(
            dto.Version.Version,
            dto.Version.LastModifiedAt,
            dto.Version.LastModifiedByDevice),
        Deleted: dto.Deleted);
}
