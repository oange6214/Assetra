using Assetra.Core.Models;
using Assetra.Infrastructure.Http;
using Assetra.Infrastructure.MarketData;
using Moq;
using Xunit;

namespace Assetra.Tests.Infrastructure.MarketData;

public class TaiwanEquityQuoteProviderTests
{
    [Fact]
    public async Task TwseProvider_BatchesTwseSymbolsIntoEquityQuotes()
    {
        var client = new Mock<ITwseClient>();
        client.Setup(c => c.FetchQuotesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(
            [
                new StockQuote(
                    "0050",
                    "元大台灣50",
                    "TWSE",
                    200m,
                    1m,
                    0.5m,
                    100,
                    199m,
                    201m,
                    198m,
                    199m,
                    DateTimeOffset.UnixEpoch),
                new StockQuote(
                    "00878",
                    "國泰永續高股息",
                    "TWSE",
                    27m,
                    0.1m,
                    0.37m,
                    100,
                    26.9m,
                    27.1m,
                    26.8m,
                    26.9m,
                    DateTimeOffset.UnixEpoch),
            ]);

        var provider = new TwseEquityQuoteProvider(client.Object);
        var results = await provider.GetQuotesAsync(
        [
            new EquityInstrumentKey("0050", "TWSE"),
            new EquityInstrumentKey("00878", "TWSE"),
        ]);

        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.Collection(
            results,
            r =>
            {
                Assert.Equal("0050", r.Value?.Instrument.Symbol);
                Assert.Equal("TWD", r.Value?.Currency);
                Assert.Equal("元大台灣50", r.Value?.Name);
            },
            r =>
            {
                Assert.Equal("00878", r.Value?.Instrument.Symbol);
                Assert.Equal("TWD", r.Value?.Currency);
                Assert.Equal("國泰永續高股息", r.Value?.Name);
            });
        client.Verify(c => c.FetchQuotesAsync(
            It.Is<IEnumerable<string>>(symbols => symbols.SequenceEqual(new[] { "0050", "00878" }))),
            Times.Once);
    }

    [Fact]
    public async Task TpexProvider_MapsTpexQuoteCurrencyAndProvider()
    {
        var client = new Mock<ITpexClient>();
        client.Setup(c => c.FetchQuotesAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(
            [
                new StockQuote(
                    "6488",
                    "環球晶",
                    "TPEX",
                    500m,
                    3m,
                    0.6m,
                    100,
                    497m,
                    501m,
                    496m,
                    497m,
                    DateTimeOffset.UnixEpoch),
            ]);

        var provider = new TpexEquityQuoteProvider(client.Object);
        var result = await provider.GetQuoteAsync(new EquityInstrumentKey("6488", "TPEX"));

        Assert.True(result.IsSuccess);
        Assert.Equal("TPEX", result.Value?.Instrument.Exchange);
        Assert.Equal("TWD", result.Value?.Currency);
        Assert.Equal("TPEX", result.Value?.SourceProvider);
    }
}
