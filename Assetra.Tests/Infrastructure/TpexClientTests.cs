using Microsoft.Extensions.Logging.Abstractions;
using RichardSzalay.MockHttp;
using Assetra.Infrastructure.Http;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class TpexClientTests
{
    [Fact]
    public async Task FetchQuotesAsync_ParsesPriceCorrectly()
    {
        const string sample = """
            [{"SecuritiesCompanyCode":"6547","CompanyName":"高端疫苗",
              "Close":"50.20","Change":"1.20","Open":"49.50",
              "High":"51.00","Low":"49.00","TradingShares":"12345"}]
            """;
        var mockHttp = new MockHttpMessageHandler();
        mockHttp.When("https://www.tpex.org.tw/openapi/v1/tpex_mainboard_quotes")
                .Respond("application/json", sample);

        var client = new TpexClient(mockHttp.ToHttpClient(), NullLogger<TpexClient>.Instance);
        var quotes = await client.FetchQuotesAsync(["6547"]);

        Assert.Single(quotes);
        Assert.Equal("6547", quotes[0].Symbol);
        Assert.Equal(50.20m, quotes[0].Price);
        Assert.Equal("TPEX", quotes[0].Exchange);
    }
}
