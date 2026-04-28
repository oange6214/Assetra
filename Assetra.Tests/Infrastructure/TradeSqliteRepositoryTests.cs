using System.IO;
using Microsoft.Data.Sqlite;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Xunit;

namespace Assetra.Tests.Infrastructure;

/// <summary>
/// Tests for TradeSqliteRepository SQL-overridden batch lookups (v0.17.5):
/// GetByPeriodAsync and GetByPortfolioEntryIdsAsync.
/// </summary>
public class TradeSqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath;

    public TradeSqliteRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trade_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private static Trade MakeIncome(DateTime date, decimal amount, Guid? entryId = null) =>
        new(Guid.NewGuid(), "", "", "", TradeType.Income, date,
            0m, 1, null, null, CashAmount: amount, PortfolioEntryId: entryId);

    // ── GetByPeriodAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetByPeriod_ReturnsTradesWithinRangeInclusive()
    {
        var repo = new TradeSqliteRepository(_dbPath);
        var inLow = MakeIncome(new DateTime(2026, 1, 1, 0, 0, 0), 100m);
        var mid = MakeIncome(new DateTime(2026, 1, 15, 12, 0, 0), 200m);
        var inHigh = MakeIncome(new DateTime(2026, 1, 31, 23, 59, 59), 300m);
        var before = MakeIncome(new DateTime(2025, 12, 31, 23, 59, 59), 1m);
        var after = MakeIncome(new DateTime(2026, 2, 1, 0, 0, 1), 2m);

        foreach (var t in new[] { inLow, mid, inHigh, before, after })
            await repo.AddAsync(t);

        var result = await repo.GetByPeriodAsync(
            new DateTime(2026, 1, 1), new DateTime(2026, 1, 31, 23, 59, 59));

        Assert.Equal(3, result.Count);
        Assert.Contains(result, t => t.Id == inLow.Id);
        Assert.Contains(result, t => t.Id == mid.Id);
        Assert.Contains(result, t => t.Id == inHigh.Id);
    }

    [Fact]
    public async Task GetByPeriod_NoMatches_ReturnsEmpty()
    {
        var repo = new TradeSqliteRepository(_dbPath);
        await repo.AddAsync(MakeIncome(new DateTime(2026, 1, 15), 100m));

        var result = await repo.GetByPeriodAsync(
            new DateTime(2025, 1, 1), new DateTime(2025, 12, 31));

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByPeriod_CrossYearRange_FiltersCorrectly()
    {
        var repo = new TradeSqliteRepository(_dbPath);
        var dec = MakeIncome(new DateTime(2025, 12, 20), 100m);
        var jan = MakeIncome(new DateTime(2026, 1, 10), 200m);
        var feb = MakeIncome(new DateTime(2026, 2, 10), 300m);

        foreach (var t in new[] { dec, jan, feb }) await repo.AddAsync(t);

        var result = await repo.GetByPeriodAsync(
            new DateTime(2025, 12, 1), new DateTime(2026, 1, 31, 23, 59, 59));

        Assert.Equal(2, result.Count);
        Assert.Contains(result, t => t.Id == dec.Id);
        Assert.Contains(result, t => t.Id == jan.Id);
    }

    // ── GetByPortfolioEntryIdsAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetByPortfolioEntryIds_EmptyCollection_ReturnsEmpty()
    {
        var repo = new TradeSqliteRepository(_dbPath);
        await repo.AddAsync(MakeIncome(DateTime.UtcNow, 100m, entryId: Guid.NewGuid()));

        var result = await repo.GetByPortfolioEntryIdsAsync(Array.Empty<Guid>());

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByPortfolioEntryIds_FiltersToMatchingEntries()
    {
        var repo = new TradeSqliteRepository(_dbPath);
        var entry1 = Guid.NewGuid();
        var entry2 = Guid.NewGuid();
        var entry3 = Guid.NewGuid();

        var t1 = MakeIncome(new DateTime(2026, 1, 1), 100m, entryId: entry1);
        var t2 = MakeIncome(new DateTime(2026, 1, 2), 200m, entryId: entry2);
        var t3 = MakeIncome(new DateTime(2026, 1, 3), 300m, entryId: entry3);
        var orphan = MakeIncome(new DateTime(2026, 1, 4), 400m, entryId: null);

        foreach (var t in new[] { t1, t2, t3, orphan }) await repo.AddAsync(t);

        var result = await repo.GetByPortfolioEntryIdsAsync(new[] { entry1, entry3 });

        Assert.Equal(2, result.Count);
        Assert.Contains(result, t => t.Id == t1.Id);
        Assert.Contains(result, t => t.Id == t3.Id);
        Assert.DoesNotContain(result, t => t.Id == t2.Id);
        Assert.DoesNotContain(result, t => t.Id == orphan.Id);
    }

    [Fact]
    public async Task GetByPortfolioEntryIds_NoMatches_ReturnsEmpty()
    {
        var repo = new TradeSqliteRepository(_dbPath);
        await repo.AddAsync(MakeIncome(DateTime.UtcNow, 100m, entryId: Guid.NewGuid()));

        var result = await repo.GetByPortfolioEntryIdsAsync(new[] { Guid.NewGuid() });

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetByPortfolioEntryIds_DuplicateIdsInQuery_DoesNotDuplicateResults()
    {
        var repo = new TradeSqliteRepository(_dbPath);
        var entry = Guid.NewGuid();
        var t = MakeIncome(new DateTime(2026, 1, 1), 100m, entryId: entry);
        await repo.AddAsync(t);

        var result = await repo.GetByPortfolioEntryIdsAsync(new[] { entry, entry, entry });

        Assert.Single(result);
        Assert.Equal(t.Id, result[0].Id);
    }
}
