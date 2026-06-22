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
    [InlineData("SPY", "NYSEARCA", "SPY")]
    [InlineData("SPY", "AMEX", "SPY")]
    [InlineData("DRAM", "BATS", "DRAM")]
    [InlineData("TEST", "IEX", "TEST")]
    [InlineData("0700", "HKEX", "0700.HK")]
    [InlineData("7203", "TSE", "7203.T")]
    [InlineData("0050", "TW", "0050.TW")]    // 比較 token 拆出的後綴（0050.TW → ("0050","TW")）→ Yahoo .TW（修盤中 0050 變直線）
    [InlineData("6488", "TWO", "6488.TWO")]  // .TWO 後綴
    [InlineData("^TWII", "TW", "^TWII")]     // 指數：原樣、不加後綴
    [InlineData("^GSPC", "NASDAQ", "^GSPC")]
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
    [InlineData("NYSEARCA", true)]
    [InlineData("AMEX", true)]
    [InlineData("BATS", true)]
    [InlineData("IEX", true)]
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
