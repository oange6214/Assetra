using System.IO;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure;

/// <summary>
/// Covers <see cref="PortfolioGroupSqliteRepository"/> + the schema migrator's seed +
/// system-protection guard. Uses a per-test temp DB file so tests are isolated.
/// </summary>
public class PortfolioGroupSqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath;

    public PortfolioGroupSqliteRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pgroup_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public async Task Init_SeedsDefaultGroup()
    {
        var repo = new PortfolioGroupSqliteRepository(_dbPath);
        var all = await repo.GetAllAsync();
        var defaultGroup = Assert.Single(all);
        Assert.Equal(PortfolioGroup.DefaultId, defaultGroup.Id);
        Assert.True(defaultGroup.IsSystem);
        Assert.Equal("預設群組", defaultGroup.Name);
    }

    [Fact]
    public async Task Init_IsIdempotent_DoesNotDuplicateDefaultGroup()
    {
        // Calling the migrator twice (via two repo ctors) must not duplicate the seed.
        _ = new PortfolioGroupSqliteRepository(_dbPath);
        _ = new PortfolioGroupSqliteRepository(_dbPath);

        var repo = new PortfolioGroupSqliteRepository(_dbPath);
        var all = await repo.GetAllAsync();
        Assert.Single(all);
        Assert.Equal(PortfolioGroup.DefaultId, all[0].Id);
    }

    [Fact]
    public async Task AddUpdateRemove_RoundTrips()
    {
        var repo = new PortfolioGroupSqliteRepository(_dbPath);
        var group = new PortfolioGroup(
            Id: Guid.NewGuid(),
            Name: "退休帳戶",
            ColorHex: "#3B82F6",
            Description: "FIRE 用",
            IconKey: "PersonClock24",
            SortOrder: 5,
            DefaultCashAccountId: null,
            IsSystem: false);

        await repo.AddAsync(group);
        var fetched = await repo.GetByIdAsync(group.Id);
        Assert.NotNull(fetched);
        Assert.Equal("退休帳戶", fetched!.Name);
        Assert.Equal("#3B82F6", fetched.ColorHex);
        Assert.Equal(5, fetched.SortOrder);
        Assert.False(fetched.IsSystem);

        var renamed = group with { Name = "退休帳戶 v2", SortOrder = 10 };
        await repo.UpdateAsync(renamed);
        var afterUpdate = await repo.GetByIdAsync(group.Id);
        Assert.Equal("退休帳戶 v2", afterUpdate!.Name);
        Assert.Equal(10, afterUpdate.SortOrder);

        await repo.RemoveAsync(group.Id);
        Assert.Null(await repo.GetByIdAsync(group.Id));
    }

    [Fact]
    public async Task RemoveAsync_OnSystemGroup_Throws()
    {
        var repo = new PortfolioGroupSqliteRepository(_dbPath);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repo.RemoveAsync(PortfolioGroup.DefaultId));
        Assert.Contains("system-protected", ex.Message);

        // Default group must still exist after the failed delete.
        var still = await repo.GetByIdAsync(PortfolioGroup.DefaultId);
        Assert.NotNull(still);
    }

    [Fact]
    public async Task RemoveAsync_OnMissingId_DoesNotThrow()
    {
        var repo = new PortfolioGroupSqliteRepository(_dbPath);
        // Should silently no-op (we just want to ensure no NRE / SQL error).
        await repo.RemoveAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task GetAllAsync_OrdersBySortOrderThenCreatedAt()
    {
        var repo = new PortfolioGroupSqliteRepository(_dbPath);
        await repo.AddAsync(new PortfolioGroup(Guid.NewGuid(), "C", SortOrder: 3));
        await repo.AddAsync(new PortfolioGroup(Guid.NewGuid(), "A", SortOrder: 1));
        await repo.AddAsync(new PortfolioGroup(Guid.NewGuid(), "B", SortOrder: 2));

        var all = await repo.GetAllAsync();
        // Default (sort 0) first, then A (1), B (2), C (3).
        Assert.Collection(all,
            g => Assert.Equal("預設群組", g.Name),
            g => Assert.Equal("A", g.Name),
            g => Assert.Equal("B", g.Name),
            g => Assert.Equal("C", g.Name));
    }
}
