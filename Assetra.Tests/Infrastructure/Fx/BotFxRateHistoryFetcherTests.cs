using System.Net;
using System.Net.Http;
using System.Text;
using Assetra.Infrastructure.Fx;
using Xunit;

namespace Assetra.Tests.Infrastructure.Fx;

/// <summary>
/// Bank of Taiwan historical FX fetcher. Tests use a stub HttpMessageHandler so
/// no real network calls happen. Monthly CSV columns:
/// [0]=資料日期(yyyyMMdd), [1]=幣別, [2]="本行買入", [3]=現金買入,
/// [4]=即期買入 (spot-buy, the column we read), … — newest day first.
/// </summary>
public sealed class BotFxRateHistoryFetcherTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpResponseMessage? Response { get; set; }
        public Exception? Throw { get; set; }
        public List<Uri> Requests { get; } = new();
        public Uri? LastRequest => Requests.Count > 0 ? Requests[^1] : null;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            if (Throw is not null)
                throw Throw;
            return Task.FromResult(Response ?? new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    /// <summary>
    /// Per-(year-month) responder so multi-month tests can return a distinct
    /// CSV per request. Falls back to 404 for unmapped months.
    /// </summary>
    private sealed class MonthlyHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _byMonth;
        public List<Uri> Requests { get; } = new();

        public MonthlyHandler(Dictionary<string, string> byMonth) => _byMonth = byMonth;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            var url = request.RequestUri!.ToString();
            foreach (var (month, csv) in _byMonth)
            {
                if (url.Contains(month, StringComparison.Ordinal))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(Encoding.UTF8.GetBytes(csv)),
                    });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static HttpClient Stub(out StubHandler handler)
    {
        handler = new StubHandler();
        return new HttpClient(handler);
    }

    /// <summary>
    /// Build a BOT monthly CSV. Header carries a leading BOM (as the live
    /// endpoint serves). Each (date, rate) becomes one data line with 即期買入
    /// in col[4].
    /// </summary>
    private static string MonthlyCsv(params (string Date, string SpotBuy)[] rows)
    {
        var sb = new StringBuilder();
        sb.Append("﻿資料日期,幣別,匯率,現金買入,即期買入,遠期,本行賣出,現金賣出,即期賣出\n");
        foreach (var (date, spot) in rows)
            sb.Append($"{date},USD,本行買入,31.20000,{spot},0,本行賣出,31.90000,31.70000\n");
        return sb.ToString();
    }

    [Fact]
    public async Task FetchAsync_ToCurrencyNotTwd_ReturnsEmptyWithoutHttp()
    {
        var http = Stub(out var handler);
        var fetcher = new BotFxRateHistoryFetcher(http);

        // BOT only quotes foreign → TWD.
        var result = await fetcher.FetchAsync("USD", "JPY", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        Assert.Empty(result);
        Assert.Null(handler.LastRequest); // no HTTP call
    }

    [Fact]
    public async Task FetchAsync_SameCurrency_ReturnsEmptyWithoutHttp()
    {
        var http = Stub(out var handler);
        var fetcher = new BotFxRateHistoryFetcher(http);

        var result = await fetcher.FetchAsync("TWD", "TWD", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        Assert.Empty(result);
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task FetchAsync_NullOrEmpty_ReturnsEmptyWithoutHttp()
    {
        var http = Stub(out var handler);
        var fetcher = new BotFxRateHistoryFetcher(http);

        Assert.Empty(await fetcher.FetchAsync("", "TWD", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)));
        Assert.Empty(await fetcher.FetchAsync("USD", " ", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 1)));
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task FetchAsync_HappyPath_ParsesSpotBuyEntries()
    {
        var http = Stub(out var handler);
        handler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(MonthlyCsv(
                ("20260611", "31.58000"),
                ("20260610", "31.55000"),
                ("20260609", "31.50000")))),
        };

        var fetcher = new BotFxRateHistoryFetcher(http);
        var entries = await fetcher.FetchAsync("USD", "TWD",
            new DateOnly(2026, 6, 9), new DateOnly(2026, 6, 11));

        Assert.Equal(3, entries.Count);
        Assert.All(entries, e => Assert.Equal("USD", e.BaseCurrency));
        Assert.All(entries, e => Assert.Equal("TWD", e.QuoteCurrency));
        Assert.All(entries, e => Assert.Equal("bot", e.Source));

        var byDate = entries.ToDictionary(e => e.Date, e => e.Rate);
        Assert.Equal(31.58000m, byDate[new DateOnly(2026, 6, 11)]);
        Assert.Equal(31.55000m, byDate[new DateOnly(2026, 6, 10)]);
        Assert.Equal(31.50000m, byDate[new DateOnly(2026, 6, 9)]);

        // URL should target the BOT monthly CSV endpoint for the right month + currency.
        Assert.NotNull(handler.LastRequest);
        Assert.Equal("https://rate.bot.com.tw/xrt/flcsv/0/2026-06/USD", handler.LastRequest!.ToString());
    }

    [Fact]
    public async Task FetchAsync_FiltersOutOfRangeDates()
    {
        var http = Stub(out var handler);
        handler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(MonthlyCsv(
                ("20260615", "31.60000"),   // out of range (after to)
                ("20260611", "31.58000"),   // in range
                ("20260605", "31.40000"),   // in range
                ("20260601", "31.30000")))), // out of range (before from)
        };

        var fetcher = new BotFxRateHistoryFetcher(http);
        var entries = await fetcher.FetchAsync("USD", "TWD",
            new DateOnly(2026, 6, 5), new DateOnly(2026, 6, 11));

        Assert.Equal(2, entries.Count);
        var dates = entries.Select(e => e.Date).ToHashSet();
        Assert.Contains(new DateOnly(2026, 6, 11), dates);
        Assert.Contains(new DateOnly(2026, 6, 5), dates);
        Assert.DoesNotContain(new DateOnly(2026, 6, 15), dates);
        Assert.DoesNotContain(new DateOnly(2026, 6, 1), dates);
    }

    [Fact]
    public async Task FetchAsync_SkipsZeroAndUnparseableSpotBuy()
    {
        var http = Stub(out var handler);
        handler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(MonthlyCsv(
                ("20260611", "31.58000"),  // valid
                ("20260610", "0"),          // zero → skip
                ("20260609", "-")))),        // unparseable → skip
        };

        var fetcher = new BotFxRateHistoryFetcher(http);
        var entries = await fetcher.FetchAsync("USD", "TWD",
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        var entry = Assert.Single(entries);
        Assert.Equal(new DateOnly(2026, 6, 11), entry.Date);
        Assert.Equal(31.58000m, entry.Rate);
    }

    [Fact]
    public async Task FetchAsync_MultiMonthRange_IssuesMultipleRequests()
    {
        var handler = new MonthlyHandler(new Dictionary<string, string>
        {
            ["2026-05"] = MonthlyCsv(("20260531", "31.40000"), ("20260515", "31.30000")),
            ["2026-06"] = MonthlyCsv(("20260615", "31.60000"), ("20260601", "31.50000")),
        });
        var http = new HttpClient(handler);

        var fetcher = new BotFxRateHistoryFetcher(http);
        var entries = await fetcher.FetchAsync("USD", "TWD",
            new DateOnly(2026, 5, 20), new DateOnly(2026, 6, 10));

        // Two months spanned → two requests.
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains(handler.Requests, u => u.ToString() == "https://rate.bot.com.tw/xrt/flcsv/0/2026-05/USD");
        Assert.Contains(handler.Requests, u => u.ToString() == "https://rate.bot.com.tw/xrt/flcsv/0/2026-06/USD");

        // Only the in-range days survive: 2026-05-31 and 2026-06-01.
        var dates = entries.Select(e => e.Date).ToHashSet();
        Assert.Equal(2, entries.Count);
        Assert.Contains(new DateOnly(2026, 5, 31), dates);
        Assert.Contains(new DateOnly(2026, 6, 1), dates);
    }

    [Fact]
    public async Task FetchAsync_OneMonth404_DoesNotAbortOtherMonths()
    {
        // 2026-05 maps to data; 2026-06 is unmapped → 404. The 404 month must not
        // abort the range; the 2026-05 data still comes back.
        var handler = new MonthlyHandler(new Dictionary<string, string>
        {
            ["2026-05"] = MonthlyCsv(("20260531", "31.40000")),
        });
        var http = new HttpClient(handler);

        var fetcher = new BotFxRateHistoryFetcher(http);
        var entries = await fetcher.FetchAsync("USD", "TWD",
            new DateOnly(2026, 5, 1), new DateOnly(2026, 6, 30));

        Assert.Equal(2, handler.Requests.Count); // both months attempted
        var entry = Assert.Single(entries);
        Assert.Equal(new DateOnly(2026, 5, 31), entry.Date);
    }

    [Fact]
    public async Task FetchAsync_NetworkException_ReturnsEmpty()
    {
        var http = Stub(out var handler);
        handler.Throw = new HttpRequestException("simulated DNS failure");
        var fetcher = new BotFxRateHistoryFetcher(http);

        var entries = await fetcher.FetchAsync("USD", "TWD",
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        Assert.Empty(entries); // contract: don't throw, return empty
    }

    [Fact]
    public async Task FetchAsync_MalformedBody_ReturnsEmpty()
    {
        var http = Stub(out var handler);
        handler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes("not even close to a BOT CSV")),
        };
        var fetcher = new BotFxRateHistoryFetcher(http);

        var entries = await fetcher.FetchAsync("USD", "TWD",
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        Assert.Empty(entries);
    }

    [Fact]
    public async Task FetchAsync_LowerCaseInput_UpperCasesInOutputAndUrl()
    {
        var http = Stub(out var handler);
        handler.Response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(MonthlyCsv(("20260611", "31.58000")))),
        };

        var fetcher = new BotFxRateHistoryFetcher(http);
        var entries = await fetcher.FetchAsync("usd", "twd",
            new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 11));

        var e = Assert.Single(entries);
        Assert.Equal("USD", e.BaseCurrency);
        Assert.Equal("TWD", e.QuoteCurrency);
        Assert.Equal("https://rate.bot.com.tw/xrt/flcsv/0/2026-06/USD", handler.LastRequest!.ToString());
    }
}
