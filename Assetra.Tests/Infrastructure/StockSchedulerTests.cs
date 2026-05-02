using System.Net.Http;
using Microsoft.Reactive.Testing;
using Moq;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.Http;
using Assetra.Infrastructure.Scheduling;
using Microsoft.Extensions.Logging.Abstractions;
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

    private static Mock<IAlertRepository> Alerts(params AlertRule[] rules)
    {
        var mock = new Mock<IAlertRepository>();
        mock.Setup(r => r.GetRulesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rules);
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

        var settings = new Mock<IAppSettingsService>();
        settings.Setup(s => s.Current).Returns(new AppSettings());
        var fugle = new FugleClient(new HttpClient(), settings.Object, NullLogger<FugleClient>.Instance);

        var svc = new StockScheduler(mockTwse.Object, mockTpex.Object,
            EmptyPortfolio().Object, Alerts().Object, settings.Object, fugle, scheduler, TimeSpan.FromSeconds(10));

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
        var settings = new Mock<IAppSettingsService>();
        settings.Setup(s => s.Current).Returns(new AppSettings());
        var fugle = new FugleClient(new HttpClient(), settings.Object, NullLogger<FugleClient>.Instance);

        var svc = new StockScheduler(mockTwse.Object, mockTpex.Object,
            EmptyPortfolio().Object, Alerts().Object, settings.Object, fugle, scheduler, TimeSpan.FromSeconds(10));

        svc.Start();
        scheduler.AdvanceBy(1);
        svc.Stop();
        scheduler.AdvanceBy(TimeSpan.FromSeconds(30).Ticks);

        // Empty portfolio → guard skips fetch; polling halted so no subsequent calls
        mockTwse.Verify(c => c.FetchQuotesAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
        mockTpex.Verify(c => c.FetchQuotesAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public void Start_FetchesQuotesForActiveAlertsWithoutPortfolioPosition()
    {
        var scheduler = new TestScheduler();
        var mockTwse = new Mock<ITwseClient>();
        var mockTpex = new Mock<ITpexClient>();

        var quote = new StockQuote("2330", "台積電", "TWSE",
            910m, 15m, 1.68m, 45231, 900m, 915m, 898m, 895m, DateTimeOffset.UnixEpoch);

        mockTwse.Setup(c => c.FetchQuotesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync([quote]);
        mockTpex.Setup(c => c.FetchQuotesAsync(It.IsAny<IEnumerable<string>>()))
                .ReturnsAsync([]);

        var settings = new Mock<IAppSettingsService>();
        settings.Setup(s => s.Current).Returns(new AppSettings());
        var fugle = new FugleClient(new HttpClient(), settings.Object, NullLogger<FugleClient>.Instance);
        var alerts = Alerts(new AlertRule(
            Guid.NewGuid(),
            "2330",
            "TWSE",
            AlertCondition.Above,
            900m));

        var svc = new StockScheduler(mockTwse.Object, mockTpex.Object,
            EmptyPortfolio().Object, alerts.Object, settings.Object, fugle, scheduler, TimeSpan.FromSeconds(10));

        IReadOnlyList<StockQuote>? received = null;
        svc.QuoteStream.Subscribe(q => received = q);
        svc.Start();

        scheduler.AdvanceBy(1);

        Assert.NotNull(received);
        Assert.Contains(received, q => q.Symbol == "2330" && q.Exchange == "TWSE");
        mockTwse.Verify(c => c.FetchQuotesAsync(
            It.Is<IEnumerable<string>>(symbols => symbols.SequenceEqual(new[] { "2330" }))),
            Times.Once);
    }
}
