using System.Net;
using System.Net.Http;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.MarketData;
using Moq;
using Xunit;

namespace Assetra.Tests.Infrastructure.MarketData;

public class TwelveDataQuoteProviderTests
{
    [Fact]
    public async Task GetQuoteAsync_MapsTwelveDataQuoteToUsdEquityQuote()
    {
        using var http = new HttpClient(new StubHttpHandler("""
            {
              "symbol": "AAPL",
              "name": "Apple Inc",
              "exchange": "NASDAQ",
              "currency": "USD",
              "datetime": "2026-05-08",
              "timestamp": 1778198400,
              "close": "183.25",
              "previous_close": "180.00",
              "change": "3.25",
              "percent_change": "1.8056"
            }
            """));
        var quota = new Mock<ITwelveDataQuotaTracker>();
        var provider = new TwelveDataQuoteProvider(
            new TwelveDataClient(http),
            Settings("demo-key"),
            quota.Object);

        var result = await provider.GetQuoteAsync(new EquityInstrumentKey("AAPL", "NASDAQ"));

        Assert.True(result.IsSuccess);
        Assert.Equal("AAPL", result.Value?.Instrument.Symbol);
        Assert.Equal("NASDAQ", result.Value?.Instrument.Exchange);
        Assert.Equal("USD", result.Value?.Currency);
        Assert.Equal("Apple Inc", result.Value?.Name);
        Assert.Equal(183.25m, result.Value?.Price);
        Assert.Equal(180m, result.Value?.PreviousClose);
        Assert.Equal(3.25m, result.Value?.Change);
        Assert.Equal(1.8056m, result.Value?.ChangePercent);
        Assert.Equal("Twelve Data", result.Value?.SourceProvider);
        Assert.True(result.Value?.IsDelayed);
        quota.Verify(q => q.RecordAsync(1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetQuoteAsync_MissingApiKeyReturnsClassifiedFailureWithoutQuotaRecord()
    {
        using var http = new HttpClient(new StubHttpHandler("""{ "unexpected": true }"""));
        var quota = new Mock<ITwelveDataQuotaTracker>();
        var provider = new TwelveDataQuoteProvider(
            new TwelveDataClient(http),
            Settings(""),
            quota.Object);

        var result = await provider.GetQuoteAsync(new EquityInstrumentKey("AAPL", "NASDAQ"));

        Assert.False(result.IsSuccess);
        Assert.Equal(MarketDataErrorCode.MissingApiKey, result.Error?.Code);
        quota.Verify(q => q.RecordAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetQuoteAsync_QuotaResponseReturnsQuotaExceeded()
    {
        using var http = new HttpClient(new StubHttpHandler("""
            {
              "code": 429,
              "message": "You have run out of API credits for today.",
              "status": "error"
            }
            """));
        var provider = new TwelveDataQuoteProvider(
            new TwelveDataClient(http),
            Settings("demo-key"),
            Mock.Of<ITwelveDataQuotaTracker>());

        var result = await provider.GetQuoteAsync(new EquityInstrumentKey("AAPL", "NASDAQ"));

        Assert.False(result.IsSuccess);
        Assert.Equal(MarketDataErrorCode.QuotaExceeded, result.Error?.Code);
        Assert.False(result.Error?.IsRetryable);
    }

    private static IAppSettingsService Settings(string apiKey)
    {
        var settings = new Mock<IAppSettingsService>();
        settings.Setup(s => s.Current).Returns(new AppSettings(TwelveDataApiKey: apiKey));
        settings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);
        return settings.Object;
    }

    private sealed class StubHttpHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json),
            });
        }
    }
}
