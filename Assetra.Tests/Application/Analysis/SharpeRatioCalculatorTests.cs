using Assetra.Application.Analysis;
using Xunit;

namespace Assetra.Tests.Application.Analysis;

public class SharpeRatioCalculatorTests
{
    [Fact]
    public void Compute_NullInputs_ReturnsNull()
    {
        var calc = new SharpeRatioCalculator();
        Assert.Null(calc.Compute(null, 0.2m, 0.02m));
        Assert.Null(calc.Compute(0.1m, null, 0.02m));
    }

    [Fact]
    public void Compute_ZeroVolatility_ReturnsNull()
    {
        var calc = new SharpeRatioCalculator();
        Assert.Null(calc.Compute(0.10m, 0.0m, 0.02m));
    }

    [Fact]
    public void Compute_TextbookCase_ReturnsExpectedRatio()
    {
        // (0.10 - 0.02) / 0.20 = 0.40
        var calc = new SharpeRatioCalculator();
        var sharpe = calc.Compute(0.10m, 0.20m, 0.02m);
        Assert.NotNull(sharpe);
        Assert.Equal(0.40m, sharpe!.Value, 4);
    }
}
