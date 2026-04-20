using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;
using Assetra.Infrastructure.Http;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class TwseClientTests
{
    private const string SampleResponse = """
        {"msgArray":[{"c":"2330","n":"台積電","z":"910.00","y":"895.00",
        "o":"900.00","h":"915.00","l":"898.00","v":"45231"}]}
        """;

    [Fact]
    public async Task FetchQuotesAsync_ParsesPriceCorrectly()
    {
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When("https://mis.twse.com.tw/stock/api/getStockInfo.jsp*")
                .Respond("application/json", SampleResponse);

        var client = new TwseClient(mockHttp.ToHttpClient(), NullLogger<TwseClient>.Instance);
        var quotes = await client.FetchQuotesAsync(["2330"]);

        Assert.Single(quotes);
        Assert.Equal("2330", quotes[0].Symbol);
        Assert.Equal(910m, quotes[0].Price);
        Assert.Equal(15m, quotes[0].Change);       // 910 - 895
        Assert.Equal("TWSE", quotes[0].Exchange);
    }

    [Fact]
    public async Task FetchQuotesAsync_WhenMarketClosed_UsesPrevCloseAsPrice()
    {
        // When CurrentPrice == "-" (market closed), PrevClose should be used as the price
        // so portfolio market values remain visible outside trading hours.
        const string dashResponse = """
            {"msgArray":[{"c":"2330","n":"台積電","z":"-","y":"895.00",
            "o":"-","h":"-","l":"-","v":"0"}]}
            """;
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When("*").Respond("application/json", dashResponse);

        var client = new TwseClient(mockHttp.ToHttpClient(), NullLogger<TwseClient>.Instance);
        var quotes = await client.FetchQuotesAsync(["2330"]);

        Assert.Single(quotes);
        Assert.Equal(895m, quotes[0].Price);   // falls back to PrevClose
        Assert.Equal(0m, quotes[0].Change);  // no change from prev close
    }

    [Fact]
    public async Task FetchQuotesAsync_WhenBothPricesInvalid_ReturnsEmpty()
    {
        // Only filter out stocks where both CurrentPrice and PrevClose are "-"
        const string noPriceResponse = """
            {"msgArray":[{"c":"9999","n":"測試","z":"-","y":"-",
            "o":"-","h":"-","l":"-","v":"0"}]}
            """;
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When("*").Respond("application/json", noPriceResponse);

        var client = new TwseClient(mockHttp.ToHttpClient(), NullLogger<TwseClient>.Instance);
        var quotes = await client.FetchQuotesAsync(["9999"]);

        Assert.Empty(quotes);
    }

    // FetchMarketIndexAsync removed in Assetra (IMarketService deleted)
}
