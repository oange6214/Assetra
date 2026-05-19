using System.IO;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Persistence;
using Assetra.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure.Sync;

/// <summary>
/// Sync-Goal-PortfolioGroup pass — covers the FinancialGoal sync surface:
/// envelope round-trip + repo GetPendingPush / MarkPushed / ApplyRemote flow.
/// </summary>
public sealed class FinancialGoalSyncTests : IDisposable
{
    private readonly string _dbPath;

    public FinancialGoalSyncTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"goal_sync_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Fact]
    public void EnvelopeRoundTrip_PreservesAllFields()
    {
        var original = new FinancialGoal(
            Guid.NewGuid(),
            "買房頭期款",
            TargetAmount: 3_000_000m,
            CurrentAmount: 250_000.50m,
            Deadline: new DateOnly(2030, 6, 30),
            Notes: "目標頭期 25%，含裝潢預算",
            LinkedAssetClass: "Cash",
            PortfolioGroupId: Guid.NewGuid());

        var version = new EntityVersion(7, DateTimeOffset.UtcNow, "deviceA");
        var envelope = FinancialGoalSyncMapper.ToEnvelope(original, version, isDeleted: false);
        var decoded = FinancialGoalSyncMapper.FromPayload(envelope);

        Assert.Equal(original.Id, decoded.Id);
        Assert.Equal(original.Name, decoded.Name);
        Assert.Equal(original.TargetAmount, decoded.TargetAmount);
        Assert.Equal(original.CurrentAmount, decoded.CurrentAmount);
        Assert.Equal(original.Deadline, decoded.Deadline);
        Assert.Equal(original.Notes, decoded.Notes);
        Assert.Equal(original.LinkedAssetClass, decoded.LinkedAssetClass);
        Assert.Equal(original.PortfolioGroupId, decoded.PortfolioGroupId);
    }

    [Fact]
    public async Task AddAsync_MarksPendingPush()
    {
        var repo = new GoalSqliteRepository(_dbPath);
        var goal = new FinancialGoal(Guid.NewGuid(), "緊急備用金", 500_000m, 100_000m, null, null);

        await repo.AddAsync(goal);
        var pending = await repo.GetPendingPushAsync();

        var env = Assert.Single(pending);
        Assert.Equal(goal.Id, env.EntityId);
        Assert.False(env.Deleted);
        Assert.Equal(FinancialGoalSyncMapper.EntityType, env.EntityType);
    }

    [Fact]
    public async Task MarkPushedAsync_ClearsPendingFlag()
    {
        var repo = new GoalSqliteRepository(_dbPath);
        var goal = new FinancialGoal(Guid.NewGuid(), "退休金", 10_000_000m, 0m, null, null);
        await repo.AddAsync(goal);

        await repo.MarkPushedAsync(new[] { goal.Id });
        var remaining = await repo.GetPendingPushAsync();

        Assert.Empty(remaining);
    }

    [Fact]
    public async Task RemoveAsync_CreatesTombstone()
    {
        var repo = new GoalSqliteRepository(_dbPath);
        var goal = new FinancialGoal(Guid.NewGuid(), "短期儲蓄", 100_000m, 50_000m, null, null);
        await repo.AddAsync(goal);
        await repo.MarkPushedAsync(new[] { goal.Id });

        await repo.RemoveAsync(goal.Id);
        var pending = await repo.GetPendingPushAsync();

        var env = Assert.Single(pending);
        Assert.Equal(goal.Id, env.EntityId);
        Assert.True(env.Deleted);
        // Soft-deleted rows shouldn't surface in GetAll.
        var all = await repo.GetAllAsync();
        Assert.Empty(all);
    }
}
