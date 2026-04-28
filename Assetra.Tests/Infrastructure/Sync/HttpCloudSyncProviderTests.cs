using System.Net;
using System.Net.Http;
using System.Text;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync.Http;
using Xunit;

namespace Assetra.Tests.Infrastructure.Sync;

public sealed class HttpCloudSyncProviderTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> RequestBodies { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null)
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            else
                RequestBodies.Add(string.Empty);
            return _responder(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private const string BaseUrl = "https://sync.example.test";

    [Fact]
    public async Task PullAsync_NoCursor_HitsBaseEndpoint_ReturnsParsedEnvelopes()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """
        {
          "envelopes": [{
            "entity_id": "11111111-1111-1111-1111-111111111111",
            "entity_type": "Trade",
            "payload": "ENC",
            "version": {
              "version": 3,
              "last_modified_at": "2026-04-28T10:00:00+00:00",
              "last_modified_by_device": "dev-A"
            },
            "deleted": false
          }],
          "next_cursor": "42"
        }
        """));
        var http = new HttpClient(handler);
        var provider = new HttpCloudSyncProvider(http, new SyncEndpointOptions(BaseUrl));

        var result = await provider.PullAsync(SyncMetadata.Empty("dev-B"));

        Assert.Single(result.Envelopes);
        Assert.Equal("Trade", result.Envelopes[0].EntityType);
        Assert.Equal("ENC", result.Envelopes[0].PayloadJson);
        Assert.Equal(3, result.Envelopes[0].Version.Version);
        Assert.Equal("42", result.NextCursor);
        Assert.Equal($"{BaseUrl}/sync/pull", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task PullAsync_WithCursor_AppendsQueryString()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"envelopes": [], "next_cursor": "99"}"""));
        var http = new HttpClient(handler);
        var provider = new HttpCloudSyncProvider(http, new SyncEndpointOptions(BaseUrl));

        await provider.PullAsync(new SyncMetadata("dev-B", Cursor: "abc 1"));

        Assert.EndsWith("/sync/pull?cursor=abc%201", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task PullAsync_AddsBearerAuthHeaderWhenTokenProvided()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"envelopes": [], "next_cursor": null}"""));
        var http = new HttpClient(handler);
        var provider = new HttpCloudSyncProvider(http, new SyncEndpointOptions(BaseUrl, "secret-token"));

        await provider.PullAsync(SyncMetadata.Empty("d"));

        var auth = handler.Requests[0].Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth!.Scheme);
        Assert.Equal("secret-token", auth.Parameter);
    }

    [Fact]
    public async Task PullAsync_NoTokenProvided_OmitsAuthHeader()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"envelopes": [], "next_cursor": null}"""));
        var http = new HttpClient(handler);
        var provider = new HttpCloudSyncProvider(http, new SyncEndpointOptions(BaseUrl));

        await provider.PullAsync(SyncMetadata.Empty("d"));

        Assert.Null(handler.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task PullAsync_4xx_Throws()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var http = new HttpClient(handler);
        var provider = new HttpCloudSyncProvider(http, new SyncEndpointOptions(BaseUrl, "x"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.PullAsync(SyncMetadata.Empty("d")));
    }

    [Fact]
    public async Task PushAsync_PostsExpectedJsonShape_ParsesAcceptedAndConflicts()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """
        {
          "accepted": ["22222222-2222-2222-2222-222222222222"],
          "conflicts": [{
            "local": {
              "entity_id": "33333333-3333-3333-3333-333333333333",
              "entity_type": "Trade",
              "payload": "L",
              "version": {"version": 1, "last_modified_at": "2026-04-28T09:00:00+00:00", "last_modified_by_device": "dev-A"},
              "deleted": false
            },
            "remote": {
              "entity_id": "33333333-3333-3333-3333-333333333333",
              "entity_type": "Trade",
              "payload": "R",
              "version": {"version": 5, "last_modified_at": "2026-04-28T10:00:00+00:00", "last_modified_by_device": "dev-B"},
              "deleted": false
            }
          }],
          "next_cursor": "100"
        }
        """));
        var http = new HttpClient(handler);
        var provider = new HttpCloudSyncProvider(http, new SyncEndpointOptions(BaseUrl, "tok"));

        var t = DateTimeOffset.Parse("2026-04-28T09:00:00+00:00");
        var batch = new[]
        {
            new SyncEnvelope(
                EntityId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                EntityType: "Trade",
                PayloadJson: "P",
                Version: new EntityVersion(1, t, "dev-A")),
        };

        var result = await provider.PushAsync(SyncMetadata.Empty("dev-A"), batch);

        Assert.Single(result.Accepted);
        Assert.Single(result.Conflicts);
        Assert.Equal("R", result.Conflicts[0].Remote.PayloadJson);
        Assert.Equal("L", result.Conflicts[0].Local.PayloadJson);
        Assert.Equal("100", result.NextCursor);

        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Contains("\"device_id\":\"dev-A\"", handler.RequestBodies[0]);
        Assert.Contains("\"entity_type\":\"Trade\"", handler.RequestBodies[0]);
        Assert.Contains("\"payload\":\"P\"", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task PushAsync_5xx_Throws()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        var http = new HttpClient(handler);
        var provider = new HttpCloudSyncProvider(http, new SyncEndpointOptions(BaseUrl));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.PushAsync(SyncMetadata.Empty("d"), Array.Empty<SyncEnvelope>()));
    }

    [Fact]
    public void Constructor_NullOrEmptyArgs_Throws()
    {
        var http = new HttpClient(new StubHandler(_ => Json(HttpStatusCode.OK, "{}")));
        Assert.Throws<ArgumentNullException>(() =>
            new HttpCloudSyncProvider(null!, new SyncEndpointOptions(BaseUrl)));
        Assert.Throws<ArgumentNullException>(() =>
            new HttpCloudSyncProvider(http, null!));
        Assert.Throws<ArgumentException>(() =>
            new HttpCloudSyncProvider(http, new SyncEndpointOptions(string.Empty)));
    }
}
