using Assetra.Infrastructure.Chart;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class ChartCalculatorTests
{
    // CalculateMa

    [Fact]
    public void CalculateMa_ReturnsNull_ForFirstPeriodMinus1Elements()
    {
        var closes = new decimal[] { 10, 20, 30, 40, 50 };
        var result = ChartCalculator.CalculateMa(closes, 3);
        Assert.Null(result[0]);
        Assert.Null(result[1]);
    }

    [Fact]
    public void CalculateMa_ReturnsCorrectAverage()
    {
        var closes = new decimal[] { 10, 20, 30, 40, 50 };
        var result = ChartCalculator.CalculateMa(closes, 3);
        Assert.Equal(20m, result[2]);  // (10+20+30)/3
        Assert.Equal(30m, result[3]);  // (20+30+40)/3
        Assert.Equal(40m, result[4]);  // (30+40+50)/3
    }

    [Fact]
    public void CalculateMa_EmptyInput_ReturnsEmpty()
    {
        var result = ChartCalculator.CalculateMa([], 5);
        Assert.Empty(result);
    }

    [Fact]
    public void CalculateMa_LessThanPeriod_AllNull()
    {
        var closes = new decimal[] { 10, 20, 30 };
        var result = ChartCalculator.CalculateMa(closes, 5);
        Assert.Equal(3, result.Count);
        Assert.All(result, v => Assert.Null(v));
    }

    [Fact]
    public void CalculateMa_ExactlyPeriod_FirstValueIsAverage()
    {
        var closes = new decimal[] { 10, 20, 30, 40, 50 };
        var result = ChartCalculator.CalculateMa(closes, 5);
        Assert.Equal(5, result.Count);
        Assert.Null(result[0]);
        Assert.Null(result[3]);
        Assert.Equal(30m, result[4]); // (10+20+30+40+50)/5 = 30
    }

    [Fact]
    public void CalculateMa_Period1_EachValueIsItself()
    {
        var closes = new decimal[] { 5, 10, 15 };
        var result = ChartCalculator.CalculateMa(closes, 1);
        Assert.Equal(3, result.Count);
        Assert.Equal(5m, result[0]);
        Assert.Equal(10m, result[1]);
        Assert.Equal(15m, result[2]);
    }

    // CalculateMacd

    [Fact]
    public void CalculateMacd_DifIsNull_WhenInsufficientData()
    {
        var closes = Enumerable.Range(1, 10).Select(i => (decimal)i).ToList();
        var result = ChartCalculator.CalculateMacd(closes);
        Assert.True(result.Dif.All(v => v is null));
    }

    [Fact]
    public void CalculateMacd_ProducesNonNullValues_WithEnoughData()
    {
        var closes = Enumerable.Range(1, 50).Select(i => (decimal)(100 + i)).ToList();
        var result = ChartCalculator.CalculateMacd(closes);
        Assert.True(result.Dif.Any(v => v is not null));
        Assert.True(result.Signal.Any(v => v is not null));
        Assert.True(result.Histogram.Any(v => v is not null));
    }

    [Fact]
    public void CalculateMacd_ShortInput_AllNullHistogram()
    {
        // MACD needs at least slow=26 bars for first DIF value
        var closes = Enumerable.Repeat(100m, 25).ToList();
        var macd = ChartCalculator.CalculateMacd(closes);
        Assert.All(macd.Histogram, v => Assert.Null(v));
    }

    [Fact]
    public void CalculateMacd_AllSamePrice_ZeroHistogram()
    {
        var closes = Enumerable.Repeat(100m, 40).ToList();
        var macd = ChartCalculator.CalculateMacd(closes);
        // With constant price, EMA fast = EMA slow = price, DIF = 0, histogram = 0
        var nonNull = macd.Histogram.Where(v => v.HasValue).ToList();
        Assert.All(nonNull, v => Assert.Equal(0m, v!.Value));
    }

    // CalculateBollingerBands

    [Fact]
    public void CalculateBollingerBands_ReturnsNull_ForFirstPeriodMinus1Elements()
    {
        var closes = Enumerable.Range(1, 25).Select(i => (decimal)i).ToList();
        var result = ChartCalculator.CalculateBollingerBands(closes, period: 20);
        // Indices 0..18 (first 19) should all be null
        for (int i = 0; i < 19; i++)
        {
            Assert.Null(result.Upper[i]);
            Assert.Null(result.Middle[i]);
            Assert.Null(result.Lower[i]);
        }
    }

    [Fact]
    public void CalculateBollingerBands_ConstantPrice_UpperEqualsLowerEqualsMiddle()
    {
        // When all prices are identical, stddev = 0, so Upper = Middle = Lower = price
        var closes = Enumerable.Repeat(100m, 25).ToList();
        var result = ChartCalculator.CalculateBollingerBands(closes, period: 20);
        var idx = 24; // last element, guaranteed non-null
        Assert.Equal(100m, result.Middle[idx]);
        Assert.Equal(100m, result.Upper[idx]);
        Assert.Equal(100m, result.Lower[idx]);
    }

    [Fact]
    public void CalculateBollingerBands_UpperGreaterThanLower_WhenPricesVary()
    {
        var closes = Enumerable.Range(1, 30).Select(i => (decimal)(100 + (i % 5))).ToList();
        var result = ChartCalculator.CalculateBollingerBands(closes, period: 20);
        var nonNull = result.Upper.Select((v, i) => (v, result.Lower[i]))
                            .Where(p => p.v.HasValue).ToList();
        Assert.NotEmpty(nonNull);
        Assert.All(nonNull, p => Assert.True(p.v!.Value > p.Item2!.Value));
    }

    // CalculateKd

    [Fact]
    public void CalculateKd_ReturnsNull_ForFirstPeriodMinus1Elements()
    {
        var highs = Enumerable.Range(1, 20).Select(i => (decimal)(100 + i)).ToList();
        var lows = Enumerable.Range(1, 20).Select(i => (decimal)(90 + i)).ToList();
        var closes = Enumerable.Range(1, 20).Select(i => (decimal)(95 + i)).ToList();
        var result = ChartCalculator.CalculateKd(highs, lows, closes, period: 9);
        // Indices 0..7 should be null
        for (int i = 0; i < 8; i++)
        {
            Assert.Null(result.K[i]);
            Assert.Null(result.D[i]);
        }
    }

    [Fact]
    public void CalculateKd_ConstantPrice_KAndDConvergeToMiddle()
    {
        // When close = high = low (all same), RSV = 50 (range=0 guard), K converges from 50
        // Since prevK = 50 and RSV = 50, K stays at 50 every bar, same for D
        var n = 30;
        var prices = Enumerable.Repeat(100m, n).ToList();
        var result = ChartCalculator.CalculateKd(prices, prices, prices, period: 9);
        var lastK = result.K[n - 1];
        var lastD = result.D[n - 1];
        Assert.NotNull(lastK);
        Assert.NotNull(lastD);
        Assert.Equal(50m, lastK!.Value, precision: 4);
        Assert.Equal(50m, lastD!.Value, precision: 4);
    }
}
