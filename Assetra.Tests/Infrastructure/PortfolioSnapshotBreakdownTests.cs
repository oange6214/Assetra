using System.IO;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class PortfolioSnapshotBreakdownTests : IDisposable
{
    private readonly string _dbPath;

    public PortfolioSnapshotBreakdownTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"assetra-snap-{Guid.NewGuid():N}.db");
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { /* best effort */ } }

    [Fact]
    public async Task Upsert_PreservesBreakdownColumns()
    {
        var repo = new PortfolioSnapshotSqliteRepository(_dbPath);
        var snap = new PortfolioDailySnapshot(
            new DateOnly(2026, 4, 28),
            TotalCost: 1_000_000m,
            MarketValue: 1_200_000m,
            Pnl: 200_000m,
            PositionCount: 5,
            Currency: "TWD",
            CashValue: 300_000m,
            EquityValue: 800_000m,
            LiabilityValue: 100_000m);

        await repo.UpsertAsync(snap);

        var loaded = await repo.GetSnapshotAsync(snap.SnapshotDate);
        Assert.NotNull(loaded);
        Assert.Equal(300_000m, loaded!.CashValue);
        Assert.Equal(800_000m, loaded.EquityValue);
        Assert.Equal(100_000m, loaded.LiabilityValue);
    }

    [Fact]
    public async Task Upsert_NullBreakdown_RoundTripsAsNull()
    {
        var repo = new PortfolioSnapshotSqliteRepository(_dbPath);
        var snap = new PortfolioDailySnapshot(
            new DateOnly(2026, 4, 28), 1_000_000m, 1_200_000m, 200_000m, 5);

        await repo.UpsertAsync(snap);

        var loaded = await repo.GetSnapshotAsync(snap.SnapshotDate);
        Assert.NotNull(loaded);
        Assert.Null(loaded!.CashValue);
        Assert.Null(loaded.EquityValue);
        Assert.Null(loaded.LiabilityValue);
    }
}
