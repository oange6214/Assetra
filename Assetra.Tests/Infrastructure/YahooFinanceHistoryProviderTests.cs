using Assetra.Infrastructure.History;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class YahooFinanceHistoryProviderTests
{
    private const string SampleJson = """
    {
      "chart": {
        "result": [{
          "timestamp": [1700000000, 1700086400],
          "indicators": {
            "quote": [{
              "open":  [100.0, 102.0],
              "high":  [105.0, 107.0],
              "low":   [99.0,  101.0],
              "close": [103.0, 106.0],
              "volume":[500000, 600000]
            }]
          }
        }]
      }
    }
    """;

    [Fact]
    public void ParseResponse_ReturnsCorrectPointCount()
    {
        var result = YahooFinanceHistoryProvider.ParseResponse(SampleJson);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ParseResponse_FirstPoint_HasCorrectValues()
    {
        var result = YahooFinanceHistoryProvider.ParseResponse(SampleJson);
        Assert.Equal(100.0m, result[0].Open);
        Assert.Equal(105.0m, result[0].High);
        Assert.Equal(99.0m, result[0].Low);
        Assert.Equal(103.0m, result[0].Close);
        Assert.Equal(500000L, result[0].Volume);
    }

    [Fact]
    public void ParseResponse_DefaultsToTaipeiTimezone_WhenExchangeOmitted()
    {
        // 1700000000 = 2023-11-14 22:13:20 UTC → Taipei +8 → 2023-11-15
        var result = YahooFinanceHistoryProvider.ParseResponse(SampleJson);
        Assert.Equal(new DateOnly(2023, 11, 15), result[0].Date);
    }

    [Fact]
    public void ParseResponse_NyseExchange_UsesNewYorkTimezone()
    {
        // 1700000000 = 2023-11-14 22:13:20 UTC → New York EST (-5) → 2023-11-14 17:13:20 → 2023-11-14
        var result = YahooFinanceHistoryProvider.ParseResponse(SampleJson, "NYSE");
        Assert.Equal(new DateOnly(2023, 11, 14), result[0].Date);
    }

    [Fact]
    public void ParseResponse_NasdaqExchange_UsesNewYorkTimezone()
    {
        var result = YahooFinanceHistoryProvider.ParseResponse(SampleJson, "NASDAQ");
        Assert.Equal(new DateOnly(2023, 11, 14), result[0].Date);
    }

    [Fact]
    public void ParseResponse_TseExchange_UsesTokyoTimezone()
    {
        // Tokyo +9 → 2023-11-15 07:13:20 → 2023-11-15
        var result = YahooFinanceHistoryProvider.ParseResponse(SampleJson, "TSE");
        Assert.Equal(new DateOnly(2023, 11, 15), result[0].Date);
    }

    [Fact]
    public void ParseResponse_UnknownExchange_FallsBackToTaipei()
    {
        var result = YahooFinanceHistoryProvider.ParseResponse(SampleJson, "BOGUS");
        Assert.Equal(new DateOnly(2023, 11, 15), result[0].Date);
    }

    [Fact]
    public void ParseResponse_SkipsNullCloseEntries()
    {
        var json = """
        {
          "chart": {
            "result": [{
              "timestamp": [1700000000, 1700086400, 1700172800],
              "indicators": {
                "quote": [{
                  "open":  [100.0, null,  102.0],
                  "high":  [105.0, null,  107.0],
                  "low":   [99.0,  null,  101.0],
                  "close": [103.0, null,  106.0],
                  "volume":[500000, null, 600000]
                }]
              }
            }]
          }
        }
        """;
        var result = YahooFinanceHistoryProvider.ParseResponse(json);
        Assert.Equal(2, result.Count);
    }
}
