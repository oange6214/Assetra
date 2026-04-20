using Assetra.Infrastructure.History;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class FinMindHistoryProviderTests
{
    private const string ValidJson = """
        {
          "msg": "success",
          "status": 200,
          "data": [
            {"date":"2026-03-03","stock_id":"2330","open":900.0,"max":920.0,"min":895.0,"close":910.0,"Trading_Volume":15000000},
            {"date":"2026-03-04","stock_id":"2330","open":910.0,"max":925.0,"min":905.0,"close":920.0,"Trading_Volume":12000000},
            {"date":"2026-03-05","stock_id":"2330","open":920.0,"max":930.0,"min":915.0,"close":915.0,"Trading_Volume":9000000}
          ]
        }
        """;

    [Fact]
    public void ParseResponse_ValidJson_ReturnsOrderedPoints()
    {
        var result = FinMindHistoryProvider.ParseResponse(ValidJson);

        Assert.Equal(3, result.Count);
        Assert.Equal(new DateOnly(2026, 3, 3), result[0].Date);
        Assert.Equal(910m, result[0].Close);
        Assert.Equal(920m, result[0].High);
        Assert.Equal(15_000_000L, result[0].Volume);
        Assert.Equal(new DateOnly(2026, 3, 5), result[2].Date);
    }

    [Fact]
    public void ParseResponse_EmptyData_ReturnsEmpty()
    {
        var json = """{"msg":"success","status":200,"data":[]}""";

        var result = FinMindHistoryProvider.ParseResponse(json);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseResponse_MissingDataProperty_ReturnsEmpty()
    {
        var json = """{"msg":"success","status":200}""";

        var result = FinMindHistoryProvider.ParseResponse(json);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseResponse_MissingCloseField_SkipsRow()
    {
        var json = """
            {
              "msg": "success",
              "status": 200,
              "data": [
                {"date":"2026-03-03","stock_id":"2330","open":900.0,"max":920.0,"min":895.0,"close":910.0,"Trading_Volume":15000000},
                {"date":"2026-03-04","stock_id":"2330","open":910.0,"max":925.0,"min":905.0,"Trading_Volume":12000000}
              ]
            }
            """;

        var result = FinMindHistoryProvider.ParseResponse(json);

        Assert.Single(result);
        Assert.Equal(new DateOnly(2026, 3, 3), result[0].Date);
    }

    [Fact]
    public void ParseResponse_UnorderedDates_ReturnsAscending()
    {
        var json = """
            {
              "msg": "success",
              "status": 200,
              "data": [
                {"date":"2026-03-05","stock_id":"2330","close":915.0},
                {"date":"2026-03-03","stock_id":"2330","close":910.0},
                {"date":"2026-03-04","stock_id":"2330","close":920.0}
              ]
            }
            """;

        var result = FinMindHistoryProvider.ParseResponse(json);

        Assert.Equal(3, result.Count);
        Assert.Equal(new DateOnly(2026, 3, 3), result[0].Date);
        Assert.Equal(new DateOnly(2026, 3, 4), result[1].Date);
        Assert.Equal(new DateOnly(2026, 3, 5), result[2].Date);
    }
}
