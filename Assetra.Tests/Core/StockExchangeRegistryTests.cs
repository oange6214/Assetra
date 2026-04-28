using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Core;

public class StockExchangeRegistryTests
{
    [Theory]
    [InlineData("TWSE", "TWD", "TW")]
    [InlineData("TPEX", "TWD", "TW")]
    [InlineData("NYSE", "USD", "US")]
    [InlineData("NASDAQ", "USD", "US")]
    [InlineData("AMEX", "USD", "US")]
    [InlineData("HKEX", "HKD", "HK")]
    [InlineData("TSE", "JPY", "JP")]
    public void TryGet_KnownExchange_ReturnsMetadata(string code, string ccy, string country)
    {
        var ex = StockExchangeRegistry.TryGet(code);
        Assert.NotNull(ex);
        Assert.Equal(ccy, ex!.DefaultCurrency);
        Assert.Equal(country, ex.Country);
    }

    [Fact]
    public void TryGet_IsCaseInsensitive()
    {
        Assert.NotNull(StockExchangeRegistry.TryGet("twse"));
        Assert.NotNull(StockExchangeRegistry.TryGet("Nasdaq"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("FAKE")]
    public void TryGet_UnknownOrBlank_ReturnsNull(string? code)
    {
        Assert.Null(StockExchangeRegistry.TryGet(code));
    }

    [Fact]
    public void ResolveDefaultCurrency_KnownExchange_ReturnsCcy()
    {
        Assert.Equal("USD", StockExchangeRegistry.ResolveDefaultCurrency("NYSE"));
    }

    [Fact]
    public void ResolveDefaultCurrency_UnknownFallsBackToDefault()
    {
        Assert.Equal("TWD", StockExchangeRegistry.ResolveDefaultCurrency("XXX"));
        Assert.Equal("EUR", StockExchangeRegistry.ResolveDefaultCurrency(null, "EUR"));
    }

    [Fact]
    public void Known_HasAllSevenExchanges()
    {
        Assert.Equal(7, StockExchangeRegistry.Known.Count);
    }
}
