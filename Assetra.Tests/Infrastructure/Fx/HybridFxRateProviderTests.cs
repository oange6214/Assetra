using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.Fx;
using Moq;
using Xunit;

namespace Assetra.Tests.Infrastructure.Fx;

/// <summary>
/// MultiCurrency-Reporting P4.1e — verifies the history-first / legacy-fallback
/// priority of <see cref="HybridFxRateProvider"/>.
/// </summary>
public sealed class HybridFxRateProviderTests
{
    private static (Mock<IFxRateHistoryService>, Mock<IFxRateProvider>) Mocks()
        => (new Mock<IFxRateHistoryService>(), new Mock<IFxRateProvider>());

    [Fact]
    public async Task GetRateAsync_HistoryHit_ReturnsHistoryWithoutLegacy()
    {
        var (hist, legacy) = Mocks();
        var d = new DateOnly(2025, 12, 31);
        hist.Setup(h => h.GetRateAsync(d, "USD", "TWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(31.5m);
        var hybrid = new HybridFxRateProvider(hist.Object, legacy.Object);

        var rate = await hybrid.GetRateAsync("USD", "TWD", d);

        Assert.Equal(31.5m, rate);
        legacy.Verify(l => l.GetRateAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetRateAsync_HistoryMiss_FallsBackToLegacy()
    {
        var (hist, legacy) = Mocks();
        var d = new DateOnly(2025, 12, 31);
        hist.Setup(h => h.GetRateAsync(d, "USD", "TWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal?)null);
        legacy.Setup(l => l.GetRateAsync("USD", "TWD", d, It.IsAny<CancellationToken>()))
            .ReturnsAsync(31.4m);
        var hybrid = new HybridFxRateProvider(hist.Object, legacy.Object);

        var rate = await hybrid.GetRateAsync("USD", "TWD", d);

        Assert.Equal(31.4m, rate);
    }

    [Fact]
    public async Task GetRateAsync_BothMiss_ReturnsNull()
    {
        var (hist, legacy) = Mocks();
        var d = new DateOnly(2025, 12, 31);
        hist.Setup(h => h.GetRateAsync(It.IsAny<DateOnly>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal?)null);
        legacy.Setup(l => l.GetRateAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((decimal?)null);
        var hybrid = new HybridFxRateProvider(hist.Object, legacy.Object);

        var rate = await hybrid.GetRateAsync("XYZ", "TWD", d);

        Assert.Null(rate);
    }

    [Fact]
    public async Task GetRateAsync_PrefersHistoryEvenWhenLegacyHasNewerData()
    {
        // Setup: history says 31.5, legacy says 31.6. Hybrid should pick 31.5
        // (history is authoritative for past dates — that's the rate that
        // actually existed). Legacy would only be checked if history was null.
        var (hist, legacy) = Mocks();
        var d = new DateOnly(2025, 12, 31);
        hist.Setup(h => h.GetRateAsync(d, "USD", "TWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(31.5m);
        legacy.Setup(l => l.GetRateAsync("USD", "TWD", d, It.IsAny<CancellationToken>()))
            .ReturnsAsync(31.6m);
        var hybrid = new HybridFxRateProvider(hist.Object, legacy.Object);

        var rate = await hybrid.GetRateAsync("USD", "TWD", d);

        Assert.Equal(31.5m, rate);
    }

    [Fact]
    public async Task GetHistoricalSeriesAsync_DelegatesToLegacy()
    {
        // Series queries go through legacy unchanged — fx_rate_history could
        // grow a parallel implementation later but isn't critical-path today.
        var (hist, legacy) = Mocks();
        var expected = new List<FxRate>
        {
            new("USD", "TWD", 30m, new DateOnly(2025, 1, 1)),
        };
        legacy.Setup(l => l.GetHistoricalSeriesAsync("USD", "TWD",
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var hybrid = new HybridFxRateProvider(hist.Object, legacy.Object);

        var series = await hybrid.GetHistoricalSeriesAsync(
            "USD", "TWD", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));

        Assert.Same(expected, series);
        // History service NOT touched — series is legacy's responsibility.
        hist.Verify(h => h.GetRateAsync(It.IsAny<DateOnly>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
