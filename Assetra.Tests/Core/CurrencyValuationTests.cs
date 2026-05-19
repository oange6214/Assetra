using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Core;

public class CurrencyValuationTests
{
    [Fact]
    public void ConvertToBase_ConvertsUsdAmountToTwd()
    {
        var rates = new Dictionary<string, decimal> { ["USD"] = 30m };

        var converted = CurrencyValuation.ConvertToBase(100m, "USD", "TWD", rates);

        Assert.Equal(3000m, converted);
    }

    [Fact]
    public void ConvertToBase_ConvertsTwdAmountToUsd()
    {
        var rates = new Dictionary<string, decimal> { ["USD"] = 30m };

        var converted = CurrencyValuation.ConvertToBase(3000m, "TWD", "USD", rates);

        Assert.Equal(100m, converted);
    }

    [Fact]
    public void ConvertToBase_UsesTwdAsCrossCurrencyBridge()
    {
        var rates = new Dictionary<string, decimal>
        {
            ["USD"] = 30m,
            ["JPY"] = 0.2m,
        };

        var converted = CurrencyValuation.ConvertToBase(10_000m, "JPY", "USD", rates);

        Assert.Equal(66.6667m, converted, 4);
    }
}
