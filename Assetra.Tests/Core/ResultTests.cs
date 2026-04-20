using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Core;

public class ResultTests
{
    [Fact]
    public void Success_SetsIsSuccessTrue()
    {
        var result = Result<int>.Success(42);
        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Failure_SetsIsSuccessFalse()
    {
        var result = Result<int>.Failure("something went wrong");
        Assert.False(result.IsSuccess);
        Assert.Equal("something went wrong", result.Error);
        Assert.Equal(default, result.Value);
    }

    [Fact]
    public void StockQuote_ConstructionWithNamedParameters_Works()
    {
        var quote = new StockQuote("2330", "台積電", "TWSE",
            Price: 910m, Change: 15m, ChangePercent: 1.68m, Volume: 45231,
            Open: 900m, High: 915m, Low: 898m, PrevClose: 895m,
            UpdatedAt: DateTimeOffset.UtcNow);
        Assert.Equal("2330", quote.Symbol);
        Assert.Equal(910m, quote.Price);
    }
}
