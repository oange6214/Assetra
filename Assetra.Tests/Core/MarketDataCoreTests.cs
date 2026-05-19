using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Core;

public class MarketDataCoreTests
{
    [Theory]
    [InlineData(" brk$b ", "BRK.B")]
    [InlineData("brk.b", "BRK.B")]
    [InlineData("0050", "0050")]
    [InlineData("00981a", "00981A")]
    public void NormalizeCanonicalSymbol_NormalizesProviderForms(string input, string expected)
    {
        Assert.Equal(expected, EquitySymbolNormalizer.NormalizeCanonicalSymbol(input));
    }

    [Theory]
    [InlineData("BRKB", "BRK.B")]
    [InlineData("BRKB", "BRK$B")]
    [InlineData("brk.b", "brk$b")]
    public void ToSearchKey_MatchesClassShareVariants(string left, string right)
    {
        Assert.Equal(
            EquitySymbolNormalizer.ToSearchKey(left),
            EquitySymbolNormalizer.ToSearchKey(right));
        Assert.True(EquitySymbolNormalizer.SymbolMatches(left, right));
    }

    [Theory]
    [InlineData("xnas", "NASDAQ")]
    [InlineData("nyse american", "AMEX")]
    [InlineData("arcx", "NYSEARCA")]
    [InlineData("twse", "TWSE")]
    public void NormalizeExchange_NormalizesAliases(string input, string expected)
    {
        Assert.Equal(expected, EquitySymbolNormalizer.NormalizeExchange(input));
    }

    [Fact]
    public void EquityInstrumentKey_NormalizesAndFormatsIdentity()
    {
        var key = new EquityInstrumentKey(" brk$b ", "xnas");

        Assert.Equal("BRK.B", key.Symbol);
        Assert.Equal("NASDAQ", key.Exchange);
        Assert.Equal("NASDAQ:BRK.B", key.Value);
        Assert.Equal("NASDAQ:BRK.B", key.ToString());
    }

    [Fact]
    public void EquityQuote_NormalizesCurrency()
    {
        var quote = new EquityQuote(
            new EquityInstrumentKey("AAPL", "NASDAQ"),
            185.2m,
            180m,
            5.2m,
            2.8889m,
            " usd ",
            new DateTimeOffset(2026, 5, 8, 20, 0, 0, TimeSpan.Zero),
            "TwelveData",
            isDelayed: true);

        Assert.Equal("USD", quote.Currency);
        Assert.True(quote.IsDelayed);
    }

    [Fact]
    public void MarketDataResult_Failure_CarriesClassifiedError()
    {
        var error = new MarketDataError(
            MarketDataErrorCode.QuotaExceeded,
            "Quota exhausted",
            Provider: "TwelveData",
            IsRetryable: false);

        var result = MarketDataResult<EquityQuote>.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(MarketDataErrorCode.QuotaExceeded, result.Error?.Code);
        Assert.True(result.Error?.IsQuotaRelated);
    }

    [Fact]
    public void EquityQuoteLegacyMapper_MapsProviderNeutralQuoteToLegacyPayload()
    {
        var updatedAt = new DateTimeOffset(2026, 5, 8, 20, 0, 0, TimeSpan.Zero);
        var quote = new EquityQuote(
            new EquityInstrumentKey("AAPL", "NASDAQ"),
            185m,
            previousClose: 180m,
            change: null,
            changePercent: null,
            currency: "USD",
            updatedAt,
            sourceProvider: "TwelveData",
            isDelayed: true);

        var legacy = EquityQuoteLegacyMapper.ToStockQuote(quote, "Apple Inc.");

        Assert.Equal("AAPL", legacy.Symbol);
        Assert.Equal("Apple Inc.", legacy.Name);
        Assert.Equal("NASDAQ", legacy.Exchange);
        Assert.Equal(185m, legacy.Price);
        Assert.Equal(5m, legacy.Change);
        Assert.Equal(5m / 180m * 100m, legacy.ChangePercent);
        Assert.Equal(180m, legacy.PrevClose);
        Assert.Equal(updatedAt, legacy.UpdatedAt);
        Assert.Equal("USD", legacy.Currency);
    }
}
