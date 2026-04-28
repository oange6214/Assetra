using Assetra.Infrastructure.History;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class YahooSymbolMapperTests
{
    [Theory]
    [InlineData("2330", "TWSE", "2330.TW")]
    [InlineData("6488", "TPEX", "6488.TWO")]
    [InlineData("AAPL", "NASDAQ", "AAPL")]
    [InlineData("BRK.B", "NYSE", "BRK.B")]
    [InlineData("SPY", "AMEX", "SPY")]
    [InlineData("0700", "HKEX", "0700.HK")]
    [InlineData("7203", "TSE", "7203.T")]
    public void ToYahooSymbol_KnownExchanges_AppliesCorrectSuffix(string symbol, string exchange, string expected)
    {
        Assert.Equal(expected, YahooSymbolMapper.ToYahooSymbol(symbol, exchange));
    }

    [Theory]
    [InlineData("nasdaq")]
    [InlineData("Twse")]
    [InlineData(" TPEX ")]
    public void ToYahooSymbol_IsCaseAndWhitespaceTolerant(string exchange)
    {
        Assert.NotEmpty(YahooSymbolMapper.ToYahooSymbol("X", exchange));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UNKNOWN")]
    public void ToYahooSymbol_UnknownOrBlankExchange_ReturnsBareSymbol(string? exchange)
    {
        Assert.Equal("AAPL", YahooSymbolMapper.ToYahooSymbol("AAPL", exchange));
    }

    [Theory]
    [InlineData("NYSE", true)]
    [InlineData("nasdaq", true)]
    [InlineData("AMEX", true)]
    [InlineData("HKEX", true)]
    [InlineData("TSE", true)]
    [InlineData("TWSE", false)]
    [InlineData("TPEX", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("UNKNOWN", false)]
    public void IsForeignExchange_DetectsNonTaiwanVenues(string? exchange, bool expected)
    {
        Assert.Equal(expected, YahooSymbolMapper.IsForeignExchange(exchange));
    }
}
