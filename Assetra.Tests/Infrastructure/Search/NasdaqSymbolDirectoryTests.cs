using Assetra.Infrastructure.Search;
using Xunit;

namespace Assetra.Tests.Infrastructure.Search;

public class NasdaqSymbolDirectoryTests
{
    private const string NasdaqListed = """
        Symbol|Security Name|Market Category|Test Issue|Financial Status|Round Lot Size|ETF|NextShares
        AAPL|Apple Inc. - Common Stock|Q|N|N|100|N|N
        QQQ|Invesco QQQ Trust, Series 1|G|N|N|100|Y|N
        TESTZ|Test Corp - Common Stock|Q|Y|N|100|N|N
        File Creation Time: 0509202618:03
        """;

    private const string OtherListed = """
        ACT Symbol|Security Name|Exchange|CQS Symbol|ETF|Round Lot Size|Test Issue|NASDAQ Symbol
        IBM|International Business Machines Corporation Common Stock|N|IBM|N|100|N|IBM
        BRK$B|Berkshire Hathaway Inc. Class B Common Stock|N|BRK.B|N|100|N|BRK/B
        ACME|Acme Holdings Common Stock|A|ACME|N|100|N|ACME
        GLD|SPDR Gold Shares|P|GLD|Y|100|N|GLD
        SPY|SPDR S&P 500 ETF Trust|P|SPY|Y|100|Y|SPY
        """;

    [Fact]
    public void Search_ReturnsNasdaqSymbol_WithUsdCurrency()
    {
        var directory = new NasdaqSymbolDirectory(NasdaqListed, OtherListed);

        var result = Assert.Single(directory.Search("AAPL"));

        Assert.Equal("AAPL", result.Symbol);
        Assert.Equal("NASDAQ", result.Exchange);
        Assert.Equal("USD", result.Currency);
        Assert.False(result.IsEtf);
    }

    [Fact]
    public void Search_ReturnsNyseSymbol_FromOtherListed()
    {
        var directory = new NasdaqSymbolDirectory(NasdaqListed, OtherListed);

        var result = Assert.Single(directory.Search("IBM"));

        Assert.Equal("IBM", result.Symbol);
        Assert.Equal("NYSE", result.Exchange);
        Assert.Equal("International Business Machines Corporation Common Stock", result.Name);
    }

    [Fact]
    public void Search_ReturnsNyseArcaEtf_FromOtherListed()
    {
        var directory = new NasdaqSymbolDirectory(NasdaqListed, OtherListed);

        var result = Assert.Single(directory.Search("GLD"));

        Assert.Equal("NYSEARCA", result.Exchange);
        Assert.True(result.IsEtf);
    }

    [Fact]
    public void Search_ReturnsAmexSymbol_FromOtherListed()
    {
        var directory = new NasdaqSymbolDirectory(NasdaqListed, OtherListed);

        var result = Assert.Single(directory.Search("ACME"));

        Assert.Equal("AMEX", result.Exchange);
        Assert.Equal("USD", result.Currency);
    }

    [Theory]
    [InlineData("BRKB")]
    [InlineData("BRK.B")]
    [InlineData("BRK$B")]
    public void Resolve_NormalizesShareClassSymbols(string query)
    {
        var directory = new NasdaqSymbolDirectory(NasdaqListed, OtherListed);

        var result = directory.Resolve(query, "NYSE");

        Assert.NotNull(result);
        Assert.Equal("BRK.B", result.Symbol);
        Assert.Equal("NYSE", result.Exchange);
    }

    [Fact]
    public void Search_ExcludesTestIssues()
    {
        var directory = new NasdaqSymbolDirectory(NasdaqListed, OtherListed);

        Assert.Empty(directory.Search("TESTZ"));
        Assert.Empty(directory.Search("SPY"));
    }
}
