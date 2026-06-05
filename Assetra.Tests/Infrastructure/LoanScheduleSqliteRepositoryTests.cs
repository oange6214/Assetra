using System.IO;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public sealed class LoanScheduleSqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath;

    public LoanScheduleSqliteRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"loan_schedule_test_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public async Task ClearPaidWithoutActiveTradeAsync_UnlocksScheduleRowsLinkedToDeletedTrades()
    {
        var scheduleRepo = new LoanScheduleSqliteRepository(_dbPath);
        var assetRepo = new AssetSqliteRepository(_dbPath);
        var tradeRepo = new TradeSqliteRepository(_dbPath);
        var assetId = Guid.NewGuid();
        var deletedTradeId = Guid.NewGuid();
        var activeTradeId = Guid.NewGuid();

        await assetRepo.AddItemAsync(new AssetItem(
            assetId,
            "台新 7y A",
            FinancialType.Liability,
            null,
            "TWD",
            new DateOnly(2024, 6, 30)));
        await tradeRepo.AddAsync(MakeLoanRepay(deletedTradeId, new DateTime(2026, 5, 30)));
        await tradeRepo.AddAsync(MakeLoanRepay(activeTradeId, new DateTime(2026, 6, 30)));
        await tradeRepo.RemoveAsync(deletedTradeId);

        await scheduleRepo.BulkInsertAsync(new[]
        {
            MakeSchedule(assetId, 1, new DateOnly(2026, 5, 30), deletedTradeId),
            MakeSchedule(assetId, 2, new DateOnly(2026, 6, 30), activeTradeId),
        });

        await scheduleRepo.ClearPaidWithoutActiveTradeAsync(assetId);

        var schedules = await scheduleRepo.GetByAssetAsync(assetId);
        var deletedLinked = Assert.Single(schedules.Where(s => s.Period == 1));
        var activeLinked = Assert.Single(schedules.Where(s => s.Period == 2));
        Assert.False(deletedLinked.IsPaid);
        Assert.Null(deletedLinked.PaidAt);
        Assert.Null(deletedLinked.TradeId);
        Assert.True(activeLinked.IsPaid);
        Assert.Equal(activeTradeId, activeLinked.TradeId);
    }

    [Fact]
    public async Task ClearPaidWithoutActiveTradeAsync_UnlocksLegacyPaidRowsWithoutTradeLink()
    {
        var scheduleRepo = new LoanScheduleSqliteRepository(_dbPath);
        var assetRepo = new AssetSqliteRepository(_dbPath);
        var assetId = Guid.NewGuid();

        await assetRepo.AddItemAsync(new AssetItem(
            assetId,
            "台新 7y A",
            FinancialType.Liability,
            null,
            "TWD",
            new DateOnly(2024, 6, 30)));

        await scheduleRepo.BulkInsertAsync(new[]
        {
            MakeSchedule(assetId, 23, new DateOnly(2026, 5, 30), tradeId: null),
        });

        await scheduleRepo.ClearPaidWithoutActiveTradeAsync(assetId);

        var schedule = Assert.Single(await scheduleRepo.GetByAssetAsync(assetId));
        Assert.False(schedule.IsPaid);
        Assert.Null(schedule.PaidAt);
        Assert.Null(schedule.TradeId);
    }

    [Fact]
    public async Task ReconcilePaidFromActiveRepaymentsAsync_DoesNotMarkRowsFromUnlinkedRepayTrades()
    {
        var scheduleRepo = new LoanScheduleSqliteRepository(_dbPath);
        var assetRepo = new AssetSqliteRepository(_dbPath);
        var tradeRepo = new TradeSqliteRepository(_dbPath);
        var assetId = Guid.NewGuid();
        var legacyTradeId = Guid.NewGuid();

        await assetRepo.AddItemAsync(new AssetItem(
            assetId,
            "台新 7y A",
            FinancialType.Liability,
            null,
            "TWD",
            new DateOnly(2024, 7, 30),
            LoanAnnualRate: 0.025m,
            LoanTermMonths: 84,
            LoanStartDate: new DateOnly(2024, 7, 30),
            LiabilitySubtype: LiabilitySubtype.Loan));
        await tradeRepo.AddAsync(MakeLoanRepay(legacyTradeId, new DateTime(2024, 8, 30)));

        await scheduleRepo.BulkInsertAsync(new[]
        {
            MakeSchedule(assetId, 1, new DateOnly(2024, 7, 30), tradeId: null, isPaid: false),
            MakeSchedule(assetId, 2, new DateOnly(2024, 8, 30), tradeId: null, isPaid: false),
            MakeSchedule(assetId, 3, new DateOnly(2024, 9, 30), tradeId: null, isPaid: false),
        });

        await scheduleRepo.ReconcilePaidFromActiveRepaymentsAsync(assetId);

        var schedules = await scheduleRepo.GetByAssetAsync(assetId);
        var first = Assert.Single(schedules.Where(s => s.Period == 1));
        var second = Assert.Single(schedules.Where(s => s.Period == 2));
        var third = Assert.Single(schedules.Where(s => s.Period == 3));

        Assert.False(first.IsPaid);
        Assert.Null(first.TradeId);
        Assert.Equal(new DateOnly(2024, 8, 30), first.DueDate);
        Assert.False(second.IsPaid);
        Assert.Null(second.TradeId);
        Assert.Equal(new DateOnly(2024, 9, 30), second.DueDate);
        Assert.False(third.IsPaid);
        Assert.Null(third.TradeId);
        Assert.Equal(new DateOnly(2024, 10, 30), third.DueDate);
    }

    private static Trade MakeLoanRepay(Guid id, DateTime date) => new(
        id,
        string.Empty,
        string.Empty,
        "台新 7y A",
        TradeType.LoanRepay,
        date,
        0m,
        1,
        null,
        null,
        CashAmount: 25_978m,
        CashAccountId: Guid.NewGuid(),
        LoanLabel: "台新 7y A",
        Principal: 22_833m,
        InterestPaid: 3_145m);

    private static LoanScheduleEntry MakeSchedule(
        Guid assetId,
        int period,
        DateOnly dueDate,
        Guid? tradeId,
        bool isPaid = true) => new(
            Guid.NewGuid(),
            assetId,
            period,
            dueDate,
            25_978m,
            22_833m,
            3_145m,
            1_000_000m,
            IsPaid: isPaid,
            PaidAt: isPaid ? dueDate.ToDateTime(TimeOnly.MinValue) : null,
            TradeId: tradeId);
}
