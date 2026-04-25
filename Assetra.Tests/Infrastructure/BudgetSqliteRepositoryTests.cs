using System.IO;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class BudgetSqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath;

    public BudgetSqliteRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"budget_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public async Task Add_Get_RoundTrips()
    {
        var repo = new BudgetSqliteRepository(_dbPath);
        var b = new Budget(Guid.NewGuid(), Guid.NewGuid(), BudgetMode.Monthly, 2026, 4, 12000m);
        await repo.AddAsync(b);

        var found = await repo.GetByIdAsync(b.Id);
        Assert.NotNull(found);
        Assert.Equal(b.CategoryId, found!.CategoryId);
        Assert.Equal(BudgetMode.Monthly, found.Mode);
        Assert.Equal(2026, found.Year);
        Assert.Equal(4, found.Month);
        Assert.Equal(12000m, found.Amount);
    }

    [Fact]
    public async Task Update_PersistsChanges()
    {
        var repo = new BudgetSqliteRepository(_dbPath);
        var b = new Budget(Guid.NewGuid(), null, BudgetMode.Monthly, 2026, 4, 5000m);
        await repo.AddAsync(b);

        var updated = b with { Amount = 8888m, Note = "updated" };
        await repo.UpdateAsync(updated);

        var found = await repo.GetByIdAsync(b.Id);
        Assert.Equal(8888m, found!.Amount);
        Assert.Equal("updated", found.Note);
    }

    [Fact]
    public async Task Remove_Deletes()
    {
        var repo = new BudgetSqliteRepository(_dbPath);
        var b = new Budget(Guid.NewGuid(), null, BudgetMode.Monthly, 2026, 4, 1000m);
        await repo.AddAsync(b);
        await repo.RemoveAsync(b.Id);

        Assert.Null(await repo.GetByIdAsync(b.Id));
    }

    [Fact]
    public async Task GetByPeriod_FiltersYearAndMonth()
    {
        var repo = new BudgetSqliteRepository(_dbPath);
        await repo.AddAsync(new Budget(Guid.NewGuid(), null, BudgetMode.Monthly, 2026, 4, 100m));
        await repo.AddAsync(new Budget(Guid.NewGuid(), null, BudgetMode.Monthly, 2026, 5, 200m));
        await repo.AddAsync(new Budget(Guid.NewGuid(), null, BudgetMode.Yearly, 2026, null, 9999m));

        var april = await repo.GetByPeriodAsync(2026, 4);
        Assert.Single(april);
        Assert.Equal(100m, april[0].Amount);

        var yearly = await repo.GetByPeriodAsync(2026, null);
        Assert.Single(yearly);
        Assert.Equal(9999m, yearly[0].Amount);
    }

    [Fact]
    public async Task NullCategoryId_RoundTrips()
    {
        var repo = new BudgetSqliteRepository(_dbPath);
        var b = new Budget(Guid.NewGuid(), null, BudgetMode.Monthly, 2026, 4, 50000m, "TWD", "Total");
        await repo.AddAsync(b);

        var found = await repo.GetByIdAsync(b.Id);
        Assert.NotNull(found);
        Assert.Null(found!.CategoryId);
        Assert.Equal("Total", found.Note);
    }
}
