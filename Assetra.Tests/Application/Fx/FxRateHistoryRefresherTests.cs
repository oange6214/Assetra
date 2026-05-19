using Assetra.Application.Fx;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Moq;
using Xunit;

namespace Assetra.Tests.Application.Fx;

/// <summary>
/// MultiCurrency-Reporting P4.1c — verifies the orchestrator's batch loop
/// over (foreign, base) pairs. Uses mocks so we don't touch network / DB.
/// </summary>
public sealed class FxRateHistoryRefresherTests
{
    private static FxRateHistoryEntry E(DateOnly d, string b, string q, decimal r)
        => new(d, b, q, r, "test", DateTimeOffset.UtcNow);

    [Fact]
    public async Task RefreshAsync_LoopsAllPairs()
    {
        var fetcher = new Mock<IFxRateHistoryFetcher>();
        fetcher.SetupGet(f => f.SourceName).Returns("test");
        fetcher.Setup(f => f.FetchAsync(It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string from, string to, DateOnly _, DateOnly __, CancellationToken _) =>
                new[] { E(DateOnly.FromDateTime(DateTime.UtcNow), from, to, 31.5m) });

        var repo = new Mock<IFxRateHistoryRepository>();
        var refresher = new FxRateHistoryRefresher(fetcher.Object, repo.Object);

        await refresher.RefreshAsync("TWD", new[] { "USD", "JPY", "HKD" });

        // 3 foreign currencies → 3 fetcher calls
        fetcher.Verify(f => f.FetchAsync("USD", "TWD",
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
        fetcher.Verify(f => f.FetchAsync("JPY", "TWD",
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
        fetcher.Verify(f => f.FetchAsync("HKD", "TWD",
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
        // Single batched upsert with 3 entries (one per pair).
        repo.Verify(r => r.UpsertRangeAsync(It.Is<IReadOnlyCollection<FxRateHistoryEntry>>(
            list => list.Count == 3), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_SkipsSameCurrencyPairs()
    {
        var fetcher = new Mock<IFxRateHistoryFetcher>();
        fetcher.Setup(f => f.FetchAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FxRateHistoryEntry>());
        var repo = new Mock<IFxRateHistoryRepository>();
        var refresher = new FxRateHistoryRefresher(fetcher.Object, repo.Object);

        // Include TWD in the foreign list — same as base, should skip
        await refresher.RefreshAsync("TWD", new[] { "USD", "TWD", "JPY" });

        fetcher.Verify(f => f.FetchAsync("USD", "TWD",
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
        fetcher.Verify(f => f.FetchAsync("TWD", "TWD",
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
        fetcher.Verify(f => f.FetchAsync("JPY", "TWD",
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_OneBadPair_DoesNotAbortBatch()
    {
        var fetcher = new Mock<IFxRateHistoryFetcher>();
        fetcher.Setup(f => f.FetchAsync("USD", "TWD",
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broken pair"));
        fetcher.Setup(f => f.FetchAsync("JPY", "TWD",
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { E(DateOnly.FromDateTime(DateTime.UtcNow), "JPY", "TWD", 0.21m) });
        var repo = new Mock<IFxRateHistoryRepository>();
        var refresher = new FxRateHistoryRefresher(fetcher.Object, repo.Object);

        await refresher.RefreshAsync("TWD", new[] { "USD", "JPY" });

        // Both pairs attempted; only good one's entries upserted.
        fetcher.Verify(f => f.FetchAsync("JPY", "TWD",
            It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.UpsertRangeAsync(It.Is<IReadOnlyCollection<FxRateHistoryEntry>>(
            list => list.Count == 1 && list.First().BaseCurrency == "JPY"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_AllPairsFail_NoUpsertCall()
    {
        var fetcher = new Mock<IFxRateHistoryFetcher>();
        fetcher.Setup(f => f.FetchAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FxRateHistoryEntry>());
        var repo = new Mock<IFxRateHistoryRepository>(MockBehavior.Strict);
        // Strict mock means any unexpected call throws. We expect zero upsert calls.
        var refresher = new FxRateHistoryRefresher(fetcher.Object, repo.Object);

        await refresher.RefreshAsync("TWD", new[] { "USD", "JPY" });

        repo.Verify(r => r.UpsertRangeAsync(It.IsAny<IReadOnlyCollection<FxRateHistoryEntry>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_NullOrEmptyBase_NoOp()
    {
        var fetcher = new Mock<IFxRateHistoryFetcher>(MockBehavior.Strict);
        var repo = new Mock<IFxRateHistoryRepository>(MockBehavior.Strict);
        var refresher = new FxRateHistoryRefresher(fetcher.Object, repo.Object);

        await refresher.RefreshAsync("", new[] { "USD" });
        await refresher.RefreshAsync(" ", new[] { "USD" });

        // Strict mocks would throw if anything was called.
    }

    [Fact]
    public async Task RefreshAsync_EmptyForeignList_NoOp()
    {
        var fetcher = new Mock<IFxRateHistoryFetcher>(MockBehavior.Strict);
        var repo = new Mock<IFxRateHistoryRepository>(MockBehavior.Strict);
        var refresher = new FxRateHistoryRefresher(fetcher.Object, repo.Object);

        await refresher.RefreshAsync("TWD", Array.Empty<string>());
    }
}
