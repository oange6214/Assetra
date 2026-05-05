using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Core;

public sealed class MoneyTests
{
    [Fact]
    public void Constructor_NormalizesCurrencyToUpper()
    {
        var m = new Money(100m, "twd");
        Assert.Equal("TWD", m.Currency);
    }

    [Fact]
    public void Constructor_TrimsCurrencyWhitespace()
    {
        var m = new Money(100m, "  USD ");
        Assert.Equal("USD", m.Currency);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_BlankCurrency_Throws(string currency)
    {
        Assert.Throws<ArgumentException>(() => new Money(1m, currency));
    }

    [Fact]
    public void Equality_CaseInsensitiveOnCurrency()
    {
        var a = new Money(50m, "twd");
        var b = new Money(50m, "TWD");
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void Equality_DifferentAmount_NotEqual()
    {
        var a = new Money(50m, "TWD");
        var b = new Money(51m, "TWD");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Add_SameCurrency_Sums()
    {
        var sum = new Money(30m, "TWD") + new Money(70m, "TWD");
        Assert.Equal(new Money(100m, "TWD"), sum);
    }

    [Fact]
    public void Add_DifferentCurrencies_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => new Money(30m, "TWD") + new Money(70m, "USD"));
        Assert.Contains("TWD", ex.Message);
        Assert.Contains("USD", ex.Message);
    }

    [Fact]
    public void Subtract_SameCurrency()
    {
        var diff = new Money(100m, "TWD") - new Money(40m, "TWD");
        Assert.Equal(new Money(60m, "TWD"), diff);
    }

    [Fact]
    public void Negate_FlipsSign()
    {
        Assert.Equal(new Money(-50m, "TWD"), -new Money(50m, "TWD"));
        Assert.Equal(new Money(-50m, "TWD"), new Money(50m, "TWD").Negate());
    }

    [Fact]
    public void Abs_AlwaysNonNegative()
    {
        Assert.Equal(new Money(50m, "TWD"), new Money(-50m, "TWD").Abs());
        Assert.Equal(new Money(50m, "TWD"), new Money( 50m, "TWD").Abs());
    }

    [Fact]
    public void Multiply_ByScalar()
    {
        Assert.Equal(new Money(150m, "USD"), new Money(50m, "USD") * 3m);
        Assert.Equal(new Money(150m, "USD"), 3m * new Money(50m, "USD"));
    }

    [Fact]
    public void Divide_ByScalar()
    {
        Assert.Equal(new Money(25m, "USD"), new Money(100m, "USD") / 4m);
    }

    [Fact]
    public void Divide_ByZero_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => new Money(10m, "USD") / 0m);
    }

    [Theory]
    [InlineData(0,  true,  false, false)]
    [InlineData(1,  false, true,  false)]
    [InlineData(-1, false, false, true)]
    public void Sign_Predicates(int amount, bool isZero, bool isPositive, bool isNegative)
    {
        var m = new Money(amount, "TWD");
        Assert.Equal(isZero, m.IsZero);
        Assert.Equal(isPositive, m.IsPositive);
        Assert.Equal(isNegative, m.IsNegative);
    }

    [Fact]
    public void Comparisons_SameCurrency()
    {
        var ten = new Money(10m, "TWD");
        var twenty = new Money(20m, "TWD");
        var tenAgain = new Money(10m, "TWD");
        Assert.True(ten < twenty);
        Assert.True(twenty > ten);
        Assert.True(ten <= tenAgain);
        Assert.True(ten >= tenAgain);
    }

    [Fact]
    public void Comparisons_DifferentCurrencies_Throw()
    {
        Assert.Throws<InvalidOperationException>(
            () => new Money(10m, "TWD") < new Money(20m, "USD"));
    }

    [Fact]
    public void Zero_FactoryCreatesZeroAmount()
    {
        var z = Money.Zero("TWD");
        Assert.True(z.IsZero);
        Assert.Equal("TWD", z.Currency);
    }

    [Fact]
    public void ToString_IncludesAmountAndCurrency()
    {
        Assert.Equal("100 TWD", new Money(100m, "TWD").ToString());
    }
}
