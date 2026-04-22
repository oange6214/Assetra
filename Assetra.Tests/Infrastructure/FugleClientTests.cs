using Assetra.Infrastructure.Http;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class FugleClientTests
{
    [Fact]
    public void ParseQuote_MapsCoreFields()
    {
        const string json = """
        {
          "symbol": "2330",
          "name": "台積電",
          "exchange": "TWSE",
          "openPrice": 950.0,
          "highPrice": 960.0,
          "lowPrice": 945.0,
          "lastPrice": 955.0,
          "previousClose": 948.0,
          "change": 7.0,
          "changePercent": 0.74,
          "lastUpdated": 1713859200000000,
          "total": {
            "tradeVolume": 123456
          }
        }
        """;

        var quote = FugleClient.ParseQuote(json);

        Assert.NotNull(quote);
        Assert.Equal("2330", quote!.Symbol);
        Assert.Equal("台積電", quote.Name);
        Assert.Equal("TWSE", quote.Exchange);
        Assert.Equal(955.0m, quote.Price);
        Assert.Equal(948.0m, quote.PrevClose);
        Assert.Equal(123456L, quote.Volume);
    }

    [Fact]
    public void ParseCandles_MapsDailySeries()
    {
        const string json = """
        {
          "data": [
            { "date": "2026-04-21", "open": 950, "high": 960, "low": 945, "close": 955, "volume": 1000 },
            { "date": "2026-04-22", "open": 956, "high": 962, "low": 952, "close": 958, "volume": 1200 }
          ]
        }
        """;

        var points = FugleClient.ParseCandles(json);

        Assert.Equal(2, points.Count);
        Assert.Equal(new DateOnly(2026, 4, 21), points[0].Date);
        Assert.Equal(955m, points[0].Close);
        Assert.Equal(1200L, points[1].Volume);
    }
}
