using System.IO;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class RecurringTransactionSqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath;

    public RecurringTransactionSqliteRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"recurring_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static RecurringTransaction Sample(string name = "Netflix") => new(
        Id: Guid.NewGuid(),
        Name: name,
        TradeType: TradeType.Withdrawal,
        Amount: 390m,
        CashAccountId: Guid.NewGuid(),
        CategoryId: Guid.NewGuid(),
        Frequency: RecurrenceFrequency.Monthly,
        Interval: 1,
        StartDate: new DateTime(2026, 1, 1),
        EndDate: null,
        GenerationMode: AutoGenerationMode.PendingConfirm,
        LastGeneratedAt: null,
        NextDueAt: new DateTime(2026, 4, 1),
        Note: "monthly subscription",
        IsEnabled: true);

    [Fact]
    public async Task Add_Get_RoundTrips()
    {
        var repo = new RecurringTransactionSqliteRepository(_dbPath);
        var r = Sample();
        await repo.AddAsync(r);

        var found = await repo.GetByIdAsync(r.Id);
        Assert.NotNull(found);
        Assert.Equal("Netflix", found!.Name);
        Assert.Equal(390m, found.Amount);
        Assert.Equal(RecurrenceFrequency.Monthly, found.Frequency);
        Assert.Equal(AutoGenerationMode.PendingConfirm, found.GenerationMode);
        Assert.True(found.IsEnabled);
    }

    [Fact]
    public async Task Update_PersistsChanges()
    {
        var repo = new RecurringTransactionSqliteRepository(_dbPath);
        var r = Sample();
        await repo.AddAsync(r);

        var updated = r with { Amount = 500m, IsEnabled = false, LastGeneratedAt = new DateTime(2026, 4, 1) };
        await repo.UpdateAsync(updated);

        var found = await repo.GetByIdAsync(r.Id);
        Assert.Equal(500m, found!.Amount);
        Assert.False(found.IsEnabled);
        Assert.Equal(new DateTime(2026, 4, 1), found.LastGeneratedAt);
    }

    [Fact]
    public async Task GetActive_OnlyReturnsEnabled()
    {
        var repo = new RecurringTransactionSqliteRepository(_dbPath);
        await repo.AddAsync(Sample("A") with { IsEnabled = true });
        await repo.AddAsync(Sample("B") with { IsEnabled = false });

        var active = await repo.GetActiveAsync();
        Assert.Single(active);
        Assert.Equal("A", active[0].Name);
    }

    [Fact]
    public async Task Remove_Deletes()
    {
        var repo = new RecurringTransactionSqliteRepository(_dbPath);
        var r = Sample();
        await repo.AddAsync(r);
        await repo.RemoveAsync(r.Id);

        Assert.Null(await repo.GetByIdAsync(r.Id));
    }
}
