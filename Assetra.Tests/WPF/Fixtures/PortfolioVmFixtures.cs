using System.Reactive.Linq;
using Moq;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure;
using Assetra.Infrastructure.Persistence;

namespace Assetra.Tests.WPF.Fixtures;

/// <summary>
/// Shared static helpers extracted from PortfolioViewModelTests during H3 Phase 3.
/// Lets future test classes (cross-VM scenarios, integration coverage) reuse the
/// canonical mock recipes without copy-pasting setup code.
/// </summary>
internal static class PortfolioVmFixtures
{
    public static PortfolioEntry MakeEntry(string symbol = "2330") =>
        new(Guid.NewGuid(), symbol, "TWSE");

    public static Dictionary<Guid, PositionSnapshot> SnapshotsFor(
        IReadOnlyList<(PortfolioEntry Entry, decimal Price, int Qty)> items) =>
        items.ToDictionary(
            x => x.Entry.Id,
            x => new PositionSnapshot(
                x.Entry.Id,
                x.Qty,
                x.Price * x.Qty,
                x.Price,
                0m,
                DateOnly.FromDateTime(DateTime.Today)));

    public static Mock<IPositionQueryService> PositionQueryMock(
        Dictionary<Guid, PositionSnapshot> snapshots)
    {
        var mock = new Mock<IPositionQueryService>();
        mock.Setup(s => s.GetAllPositionSnapshotsAsync())
            .ReturnsAsync(snapshots);
        mock.Setup(s => s.GetPositionAsync(It.IsAny<Guid>()))
            .Returns<Guid>(id => Task.FromResult(
                snapshots.TryGetValue(id, out var s) ? s : null));
        mock.Setup(s => s.ComputeRealizedPnlAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<decimal>(),
                It.IsAny<decimal>(), It.IsAny<decimal>()))
            .ReturnsAsync(0m);
        return mock;
    }

    public static (PortfolioSnapshotService Svc, Mock<IPortfolioSnapshotRepository> Repo) SnapshotStubs()
    {
        var repo = new Mock<IPortfolioSnapshotRepository>();
        repo.Setup(r => r.GetSnapshotsAsync(It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>()))
            .ReturnsAsync(Array.Empty<PortfolioDailySnapshot>());
        repo.Setup(r => r.UpsertAsync(It.IsAny<PortfolioDailySnapshot>()))
            .Returns(Task.CompletedTask);
        return (new PortfolioSnapshotService(repo.Object), repo);
    }

    public static Mock<IStockService> SilentStockService()
    {
        var mock = new Mock<IStockService>();
        mock.Setup(s => s.QuoteStream)
            .Returns(Observable.Never<IReadOnlyList<StockQuote>>());
        return mock;
    }

    public static (Mock<IPortfolioPositionLogRepository> LogRepo, PortfolioBackfillService Backfill)
        BackfillStubs(Mock<IPortfolioSnapshotRepository> snapshotRepo)
    {
        var logRepo = new Mock<IPortfolioPositionLogRepository>();
        logRepo.Setup(r => r.HasAnyAsync()).ReturnsAsync(true);
        logRepo.Setup(r => r.LogAsync(It.IsAny<PortfolioPositionLog>())).Returns(Task.CompletedTask);
        logRepo.Setup(r => r.LogBatchAsync(It.IsAny<IEnumerable<PortfolioPositionLog>>())).Returns(Task.CompletedTask);
        logRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<PortfolioPositionLog>());

        var historyProvider = new Mock<IStockHistoryProvider>();
        var backfill = new PortfolioBackfillService(logRepo.Object, snapshotRepo.Object, historyProvider.Object);
        return (logRepo, backfill);
    }
}
