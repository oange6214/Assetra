using Assetra.Application.Fx;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Moq;
using Xunit;

namespace Assetra.Tests.Application.Fx;

/// <summary>
/// MultiCurrency-Reporting P4.1 — verifies the application-layer service's
/// cache + nearest-date fallback behavior.
/// </summary>
public sealed class FxRateHistoryServiceTests
{
    private static FxRateHistoryEntry E(DateOnly d, string b, string q, decimal r)
        => new(d, b, q, r, "test", DateTimeOffset.UtcNow);

    [Fact]
    public async Task GetRateAsync_SameCurrency_ReturnsOneWithoutHittingRepo()
    {
        var repo = new Mock<IFxRateHistoryRepository>(MockBehavior.Strict);
        var svc = new FxRateHistoryService(repo.Object);

        var rate = await svc.GetRateAsync(new DateOnly(2025, 12, 31), "TWD", "TWD");

        Assert.Equal(1m, rate);
        repo.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetRateAsync_NullOrEmptyCurrency_ReturnsNull()
    {
        var repo = new Mock<IFxRateHistoryRepository>(MockBehavior.Strict);
        var svc = new FxRateHistoryService(repo.Object);

        Assert.Null(await svc.GetRateAsync(new DateOnly(2025, 1, 1), "", "TWD"));
        Assert.Null(await svc.GetRateAsync(new DateOnly(2025, 1, 1), "USD", " "));
    }

    [Fact]
    public async Task GetRateAsync_ExactMatch_BypassesFallback()
    {
        var d = new DateOnly(2025, 12, 31);
        var repo = new Mock<IFxRateHistoryRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetAsync(d, "USD", "TWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(E(d, "USD", "TWD", 31.5m));
        var svc = new FxRateHistoryService(repo.Object);

        var rate = await svc.GetRateAsync(d, "USD", "TWD");

        Assert.Equal(31.5m, rate);
        repo.Verify(r => r.GetNearestAsync(It.IsAny<DateOnly>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetRateAsync_ExactMiss_FallsBackToNearest()
    {
        var d = new DateOnly(2025, 12, 31);
        var monday = new DateOnly(2025, 12, 29);
        var repo = new Mock<IFxRateHistoryRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetAsync(d, "USD", "TWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync((FxRateHistoryEntry?)null);
        repo.Setup(r => r.GetNearestAsync(d, "USD", "TWD", 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(E(monday, "USD", "TWD", 31.2m));
        var svc = new FxRateHistoryService(repo.Object);

        var rate = await svc.GetRateAsync(d, "USD", "TWD");

        Assert.Equal(31.2m, rate);
    }

    [Fact]
    public async Task GetRateAsync_NoRateAvailable_ReturnsNull()
    {
        var d = new DateOnly(2025, 12, 31);
        var repo = new Mock<IFxRateHistoryRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetAsync(d, "USD", "TWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync((FxRateHistoryEntry?)null);
        repo.Setup(r => r.GetNearestAsync(d, "USD", "TWD", 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync((FxRateHistoryEntry?)null);
        var svc = new FxRateHistoryService(repo.Object);

        var rate = await svc.GetRateAsync(d, "USD", "TWD");

        Assert.Null(rate);
    }

    [Fact]
    public async Task GetRateAsync_RepeatedLookups_HitCache()
    {
        var d = new DateOnly(2025, 12, 31);
        var repo = new Mock<IFxRateHistoryRepository>();
        repo.Setup(r => r.GetAsync(d, "USD", "TWD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(E(d, "USD", "TWD", 31.5m));
        var svc = new FxRateHistoryService(repo.Object);

        var a = await svc.GetRateAsync(d, "USD", "TWD");
        var b = await svc.GetRateAsync(d, "USD", "TWD");
        var c = await svc.GetRateAsync(d, "usd", "twd"); // case-insensitive cache hit

        Assert.Equal(31.5m, a);
        Assert.Equal(a, b);
        Assert.Equal(a, c);
        // Repo should have been called exactly ONCE; subsequent reads come from cache.
        repo.Verify(r => r.GetAsync(d, "USD", "TWD", It.IsAny<CancellationToken>()), Times.Once);
    }
}
