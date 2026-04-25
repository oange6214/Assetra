using System.IO;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class PendingRecurringEntrySqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath;

    public PendingRecurringEntrySqliteRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"pending_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static PendingRecurringEntry Sample(PendingStatus status = PendingStatus.Pending) => new(
        Id: Guid.NewGuid(),
        RecurringSourceId: Guid.NewGuid(),
        DueDate: new DateTime(2026, 4, 1),
        Amount: 390m,
        TradeType: TradeType.Withdrawal,
        CashAccountId: Guid.NewGuid(),
        CategoryId: Guid.NewGuid(),
        Note: "Netflix",
        Status: status);

    [Fact]
    public async Task Add_Get_RoundTrips()
    {
        var repo = new PendingRecurringEntrySqliteRepository(_dbPath);
        var e = Sample();
        await repo.AddAsync(e);

        var found = await repo.GetByIdAsync(e.Id);
        Assert.NotNull(found);
        Assert.Equal(390m, found!.Amount);
        Assert.Equal(PendingStatus.Pending, found.Status);
    }

    [Fact]
    public async Task GetByStatus_FiltersCorrectly()
    {
        var repo = new PendingRecurringEntrySqliteRepository(_dbPath);
        await repo.AddAsync(Sample(PendingStatus.Pending));
        await repo.AddAsync(Sample(PendingStatus.Pending));
        await repo.AddAsync(Sample(PendingStatus.Confirmed));

        var pending = await repo.GetByStatusAsync(PendingStatus.Pending);
        Assert.Equal(2, pending.Count);

        var confirmed = await repo.GetByStatusAsync(PendingStatus.Confirmed);
        Assert.Single(confirmed);
    }

    [Fact]
    public async Task Update_MarksConfirmedWithTradeId()
    {
        var repo = new PendingRecurringEntrySqliteRepository(_dbPath);
        var e = Sample();
        await repo.AddAsync(e);

        var tradeId = Guid.NewGuid();
        var resolved = e with
        {
            Status = PendingStatus.Confirmed,
            GeneratedTradeId = tradeId,
            ResolvedAt = new DateTime(2026, 4, 25),
        };
        await repo.UpdateAsync(resolved);

        var found = await repo.GetByIdAsync(e.Id);
        Assert.Equal(PendingStatus.Confirmed, found!.Status);
        Assert.Equal(tradeId, found.GeneratedTradeId);
    }
}
