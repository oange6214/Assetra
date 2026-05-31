using System.Net;
using System.Net.Http;
using System.Text;
using Assetra.Infrastructure.Fx;
using Xunit;

namespace Assetra.Tests.Infrastructure.Fx;

/// <summary>
/// MultiCurrency-Reporting P4.1b — Yahoo fetcher. Tests use a stub
/// HttpMessageHandler so no real network calls happen.
/// </summary>
public sealed class YahooFxRateHistoryFetcherTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpResponseMessage? Response { get; set; }
        public Exception? Throw { get; set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            if (Throw is not null)
                throw Throw;
            return Task.FromResult(Response ?? new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static HttpClient Stub(out StubHandler handler)
    {
        handler = new StubHandler();
        return new HttpClient(handler);
    }

    private static string SampleResponse(long[] timestamps, double?[] closes)
    {
        var sb = new StringBuilder();
        sb.Append("{\"chart\":{\"result\":[{");
        sb.Append("\"timestamp\":[");
        sb.Append(string.Join(",", timestamps));
        sb.Append("],\"indicators\":{\"quote\":[{\"close\":[");
        sb.Append(string.Join(",", closes.Select(c => c is null ? "null" : c.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        sb.Append("]}]}}]}}");
        return sb.ToString();
    }

    [Fact]
    public async Task FetchAsync_SameCurrency_ReturnsEmptyWithoutHttp()
    {
        var http = Stub(out var handler);
        var fetcher = new YahooFxRateHistoryFetcher(http);

        var result = await fetcher.FetchAsync("USD", "USD", new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 31));

        Assert.Empty(result);
        Assert.Null(handler.LastRequest); // no HTTP call
    }

    [Fact]
    public async Task FetchAsync_NullOrEmpty_ReturnsEmptyWithoutHttp()
    {
        var http = Stub(out var handler);
        var fetcher = new YahooFxRateHistoryFetcher(http);

        Assert.Empty(await fetcher.FetchAsync("", "TWD", new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 1)));
        Assert.Empty(await fetcher.FetchAsync("USD", " ", new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 1)));
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task FetchAsync_HappyPath_ParsesEntries()
    {
        var dec29 = new DateTimeOffset(new DateTime(2025, 12, 29, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var dec30 = new DateTimeOffset(new DateTime(2025, 12, 30, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var dec31 = new DateTimeOffset(new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeSeconds();

        var http = Stub(out var handler);
        handler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SampleResponse(
                new[] { dec29, dec30, dec31 },
                new double?[] { 31.2, 31.3, 31.5 })),
        };

        var fetcher = new YahooFxRateHistoryFetcher(http);
        var entries = await fetcher.FetchAsync("USD", "TWD",
            new DateOnly(2025, 12, 29), new DateOnly(2025, 12, 31));

        Assert.Equal(3, entries.Count);
        Assert.Equal("USD", entries[0].BaseCurrency);
        Assert.Equal("TWD", entries[0].QuoteCurrency);
        Assert.Equal("yahoo", entries[0].Source);
        Assert.Equal(31.2m, entries[0].Rate);
        Assert.Equal(31.5m, entries[2].Rate);
        // URL should contain the Yahoo symbol "USDTWD=X"
        Assert.NotNull(handler.LastRequest);
        Assert.Contains("USDTWD%3DX", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task FetchAsync_NullCloseEntries_AreSkipped()
    {
        var dec29 = new DateTimeOffset(new DateTime(2025, 12, 29, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var dec30 = new DateTimeOffset(new DateTime(2025, 12, 30, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var dec31 = new DateTimeOffset(new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeSeconds();

        var http = Stub(out var handler);
        handler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SampleResponse(
                new[] { dec29, dec30, dec31 },
                new double?[] { 31.2, null, 31.5 })), // gap day
        };

        var fetcher = new YahooFxRateHistoryFetcher(http);
        var entries = await fetcher.FetchAsync("USD", "TWD",
            new DateOnly(2025, 12, 29), new DateOnly(2025, 12, 31));

        Assert.Equal(2, entries.Count); // null day skipped
    }

    [Fact]
    public async Task FetchAsync_Http404_ReturnsEmpty()
    {
        var http = Stub(out var handler);
        handler.Response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"chart\":{\"error\":{\"code\":\"Not Found\"}}}"),
        };

        var fetcher = new YahooFxRateHistoryFetcher(http);
        var entries = await fetcher.FetchAsync("XXX", "TWD",
            new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 1));

        Assert.Empty(entries);
    }

    [Fact]
    public async Task FetchAsync_Http500_ReturnsEmpty()
    {
        var http = Stub(out var handler);
        handler.Response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        var fetcher = new YahooFxRateHistoryFetcher(http);

        var entries = await fetcher.FetchAsync("USD", "TWD",
            new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 1));

        Assert.Empty(entries);
    }

    [Fact]
    public async Task FetchAsync_NetworkException_ReturnsEmpty()
    {
        var http = Stub(out var handler);
        handler.Throw = new HttpRequestException("simulated DNS failure");
        var fetcher = new YahooFxRateHistoryFetcher(http);

        var entries = await fetcher.FetchAsync("USD", "TWD",
            new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 1));

        Assert.Empty(entries); // contract: don't throw, return empty
    }

    [Fact]
    public async Task FetchAsync_MalformedJson_ReturnsEmpty()
    {
        var http = Stub(out var handler);
        handler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not even close to JSON"),
        };
        var fetcher = new YahooFxRateHistoryFetcher(http);

        var entries = await fetcher.FetchAsync("USD", "TWD",
            new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 1));

        Assert.Empty(entries);
    }

    [Fact]
    public async Task FetchAsync_LowerCaseInput_UpperCasesInOutput()
    {
        var dec31 = new DateTimeOffset(new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var http = Stub(out var handler);
        handler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SampleResponse(new[] { dec31 }, new double?[] { 31.5 })),
        };

        var fetcher = new YahooFxRateHistoryFetcher(http);
        var entries = await fetcher.FetchAsync("usd", "twd",
            new DateOnly(2025, 12, 31), new DateOnly(2025, 12, 31));

        var e = Assert.Single(entries);
        Assert.Equal("USD", e.BaseCurrency);
        Assert.Equal("TWD", e.QuoteCurrency);
    }
}
