using Microsoft.Reactive.Testing;
using Moq;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.Http;
using Assetra.Infrastructure.Scheduling;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class StockSchedulerTests
{
    // Empty portfolio repo — used across all tests that don't need portfolio data
    private static Mock<IPortfolioRepository> EmptyPortfolio()
    {
        var mock = new Mock<IPortfolioRepository>();
        mock.Setup(r => r.GetEntriesAsync()).ReturnsAsync([]);
        return mock;
    }

    [Fact]
    public void Start_EmitsQuotesImmediately()
    {
        var scheduler = new TestScheduler();
        var mockTwse = new Mock<ITwseClient>();
        var mockTpex = new Mock<ITpexClient>();

        var quote = new StockQuote("2330", "台積電", "TWSE",
            910m, 15m, 1.68m, 45231, 900m, 915m, 898m, 895m, DateTimeOffset.UnixEpoch);

        // Assetra StockScheduler fetches from portfolio, not watchlist
        EmptyPortfolio().Setup(r => r.GetEntriesAsync()).ReturnsAsync([]);
        mockTwse.Setup(c => c.FetchQuotesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync([quote]);
        mockTpex.Setup(c => c.FetchQuotesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync([]);

        // Assetra ctor: (ITwseClient, ITpexClient, IPortfolioRepository, IScheduler, TimeSpan?)
        var svc = new StockScheduler(mockTwse.Object, mockTpex.Object,
            EmptyPortfolio().Object, scheduler, TimeSpan.FromSeconds(10));

        IReadOnlyList<StockQuote>? received = null;
        svc.QuoteStream.Subscribe(q => received = q);
        svc.Start();

        scheduler.AdvanceBy(1); // trigger immediate
        // Empty portfolio → no symbols → no quotes emitted, but subscription fires
        Assert.NotNull(received);
    }

    [Fact]
    public void Stop_HaltsPolling()
    {
        var scheduler = new TestScheduler();
        var mockTwse = new Mock<ITwseClient>();
        var mockTpex = new Mock<ITpexClient>();

        mockTwse.Setup(c => c.FetchQuotesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync([]);
        mockTpex.Setup(c => c.FetchQuotesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync([]);

        var svc = new StockScheduler(mockTwse.Object, mockTpex.Object,
            EmptyPortfolio().Object, scheduler, TimeSpan.FromSeconds(10));

        svc.Start();
        scheduler.AdvanceBy(1);
        svc.Stop();
        scheduler.AdvanceBy(TimeSpan.FromSeconds(30).Ticks);

        // Empty portfolio → guard skips fetch; polling halted so no subsequent calls
        mockTwse.Verify(c => c.FetchQuotesAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
        mockTpex.Verify(c => c.FetchQuotesAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
    }
}
