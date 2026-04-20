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
