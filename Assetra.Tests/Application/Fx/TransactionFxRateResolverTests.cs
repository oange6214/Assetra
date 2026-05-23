using Assetra.Application.Fx;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Moq;
using Xunit;

namespace Assetra.Tests.Application.Fx;

public sealed class TransactionFxRateResolverTests
{
    private static FxRateHistoryEntry Entry(DateOnly date, decimal rate, string source = "yahoo")
        => new(date, "USD", "TWD", rate, source, DateTimeOffset.UtcNow);

    [Fact]
    public async Task ResolveAsync_SameCurrency_ReturnsOneWithoutHistoryLookup()
    {
        var history = new Mock<IFxRateHistoryService>(MockBehavior.Strict);
        var resolver = new TransactionFxRateResolver(history.Object);

        var quote = await resolver.ResolveAsync(new DateOnly(2026, 5, 8), "USD", "usd");

        Assert.Equal(TransactionFxQuoteStatus.SameCurrency, quote.Status);
        Assert.Equal(1m, quote.Rate);
        Assert.Equal(new DateOnly(2026, 5, 8), quote.RateDate);
        Assert.False(quote.IsEstimated);
        history.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ResolveAsync_HistoricalRate_ReturnsRateDateAndSource()
    {
        var tradeDate = new DateOnly(2026, 5, 8);
        var history = new Mock<IFxRateHistoryService>(MockBehavior.Strict);
        history.Setup(x => x.GetEntryAsync(tradeDate, "USD", "TWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Entry(tradeDate, 32.335m, "yahoo"));
        var resolver = new TransactionFxRateResolver(history.Object);

        var quote = await resolver.ResolveAsync(tradeDate, "usd", "twd");

        Assert.Equal(TransactionFxQuoteStatus.Resolved, quote.Status);
        Assert.Equal("USD", quote.FromCurrency);
        Assert.Equal("TWD", quote.ToCurrency);
        Assert.Equal(32.335m, quote.Rate);
        Assert.Equal(tradeDate, quote.RateDate);
        Assert.Equal("yahoo", quote.Source);
        Assert.False(quote.IsEstimated);
    }

    [Fact]
    public async Task ResolveAsync_NearestFallback_MarksEstimated()
    {
        var tradeDate = new DateOnly(2026, 5, 10);
        var nearest = new DateOnly(2026, 5, 8);
        var history = new Mock<IFxRateHistoryService>(MockBehavior.Strict);
        history.Setup(x => x.GetEntryAsync(tradeDate, "USD", "TWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Entry(nearest, 32.1m, "yahoo"));
        var resolver = new TransactionFxRateResolver(history.Object);

        var quote = await resolver.ResolveAsync(tradeDate, "USD", "TWD");

        Assert.Equal(TransactionFxQuoteStatus.Resolved, quote.Status);
        Assert.Equal(32.1m, quote.Rate);
        Assert.Equal(nearest, quote.RateDate);
        Assert.True(quote.IsEstimated);
    }

    [Fact]
    public async Task ResolveAsync_MissingRate_ReturnsUnavailableWithoutZero()
    {
        var tradeDate = new DateOnly(2026, 5, 8);
        var history = new Mock<IFxRateHistoryService>(MockBehavior.Strict);
        history.Setup(x => x.GetEntryAsync(tradeDate, "USD", "TWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync((FxRateHistoryEntry?)null);
        var resolver = new TransactionFxRateResolver(history.Object);

        var quote = await resolver.ResolveAsync(tradeDate, "USD", "TWD");

        Assert.Equal(TransactionFxQuoteStatus.Unavailable, quote.Status);
        Assert.Null(quote.Rate);
        Assert.Null(quote.RateDate);
        Assert.Null(quote.Source);
    }
}
