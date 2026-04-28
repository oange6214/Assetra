using System.IO;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class PortfolioEventSqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath;

    public PortfolioEventSqliteRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"assetra-evt-{Guid.NewGuid():N}.db");
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { /* best effort */ } }

    [Fact]
    public async Task AddAndGetAll_RoundTrips()
    {
        var repo = new PortfolioEventSqliteRepository(_dbPath);
        var evt = new PortfolioEvent(
            Guid.NewGuid(), new DateOnly(2026, 5, 1),
            PortfolioEventKind.LargeTrade, "Buy 2330 × 200",
            "成交金額 120,000", 120_000m, "2330");

        await repo.AddAsync(evt);

        var all = await repo.GetAllAsync();
        var single = Assert.Single(all);
        Assert.Equal(evt.Id, single.Id);
        Assert.Equal(PortfolioEventKind.LargeTrade, single.Kind);
        Assert.Equal(120_000m, single.Amount);
        Assert.Equal("2330", single.Symbol);
    }

    [Fact]
    public async Task NullableFields_RoundTrip()
    {
        var repo = new PortfolioEventSqliteRepository(_dbPath);
        var evt = new PortfolioEvent(
            Guid.NewGuid(), new DateOnly(2026, 5, 1),
            PortfolioEventKind.UserNote, "備註", null, null, null);

        await repo.AddAsync(evt);
        var loaded = (await repo.GetAllAsync()).Single();
        Assert.Null(loaded.Description);
        Assert.Null(loaded.Amount);
        Assert.Null(loaded.Symbol);
    }

    [Fact]
    public async Task GetRange_FiltersByDate()
    {
        var repo = new PortfolioEventSqliteRepository(_dbPath);
        await repo.AddAsync(new PortfolioEvent(Guid.NewGuid(), new DateOnly(2026, 1, 1), PortfolioEventKind.UserNote, "A", null));
        await repo.AddAsync(new PortfolioEvent(Guid.NewGuid(), new DateOnly(2026, 6, 1), PortfolioEventKind.UserNote, "B", null));
        await repo.AddAsync(new PortfolioEvent(Guid.NewGuid(), new DateOnly(2026, 12, 1), PortfolioEventKind.UserNote, "C", null));

        var inRange = await repo.GetRangeAsync(new DateOnly(2026, 3, 1), new DateOnly(2026, 9, 1));
        var single = Assert.Single(inRange);
        Assert.Equal("B", single.Label);
    }

    [Fact]
    public async Task Update_PersistsChanges()
    {
        var repo = new PortfolioEventSqliteRepository(_dbPath);
        var evt = new PortfolioEvent(
            Guid.NewGuid(), new DateOnly(2026, 5, 1),
            PortfolioEventKind.UserNote, "原始", "old", null, null);
        await repo.AddAsync(evt);
        await repo.UpdateAsync(evt with { Label = "新", Description = "new" });

        var loaded = (await repo.GetAllAsync()).Single();
        Assert.Equal("新", loaded.Label);
        Assert.Equal("new", loaded.Description);
    }

    [Fact]
    public async Task Remove_DeletesRow()
    {
        var repo = new PortfolioEventSqliteRepository(_dbPath);
        var evt = new PortfolioEvent(
            Guid.NewGuid(), new DateOnly(2026, 5, 1),
            PortfolioEventKind.UserNote, "X", null);
        await repo.AddAsync(evt);
        await repo.RemoveAsync(evt.Id);

        Assert.Empty(await repo.GetAllAsync());
    }
}
