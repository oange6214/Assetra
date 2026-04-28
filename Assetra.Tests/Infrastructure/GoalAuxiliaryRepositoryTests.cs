using System.IO;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class GoalAuxiliaryRepositoryTests : IDisposable
{
    private readonly string _dbPath;

    public GoalAuxiliaryRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"assetra-goal-aux-{Guid.NewGuid():N}.db");
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { /* best effort */ } }

    private async Task<Guid> CreateGoalAsync()
    {
        var goalRepo = new GoalSqliteRepository(_dbPath);
        var goal = new FinancialGoal(Guid.NewGuid(), "Test Goal", 500_000m, 0m, null, null);
        await goalRepo.AddAsync(goal);
        return goal.Id;
    }

    [Fact]
    public async Task Milestone_AddAndGetByGoal_RoundTrips()
    {
        var repo = new GoalMilestoneSqliteRepository(_dbPath);
        var goalId = await CreateGoalAsync();
        var m = new GoalMilestone(Guid.NewGuid(), goalId, new DateOnly(2027, 6, 30), 250_000m, "Halfway", false);
        await repo.AddAsync(m);

        var loaded = await repo.GetByGoalAsync(goalId);
        var single = Assert.Single(loaded);
        Assert.Equal(m.Id, single.Id);
        Assert.Equal(250_000m, single.TargetAmount);
        Assert.Equal("Halfway", single.Label);
        Assert.False(single.IsAchieved);
    }

    [Fact]
    public async Task Milestone_Update_PersistsAchievement()
    {
        var repo = new GoalMilestoneSqliteRepository(_dbPath);
        var goalId = await CreateGoalAsync();
        var m = new GoalMilestone(Guid.NewGuid(), goalId, new DateOnly(2027, 6, 30), 250_000m, "Halfway", false);
        await repo.AddAsync(m);
        await repo.UpdateAsync(m with { IsAchieved = true });

        var loaded = (await repo.GetByGoalAsync(goalId)).Single();
        Assert.True(loaded.IsAchieved);
    }

    [Fact]
    public async Task Milestone_Remove_DeletesRow()
    {
        var repo = new GoalMilestoneSqliteRepository(_dbPath);
        var goalId = await CreateGoalAsync();
        var m = new GoalMilestone(Guid.NewGuid(), goalId, new DateOnly(2027, 6, 30), 250_000m, "X", false);
        await repo.AddAsync(m);
        await repo.RemoveAsync(m.Id);

        Assert.Empty(await repo.GetByGoalAsync(goalId));
    }

    [Fact]
    public async Task FundingRule_AddAndGetByGoal_RoundTrips()
    {
        var repo = new GoalFundingRuleSqliteRepository(_dbPath);
        var goalId = await CreateGoalAsync();
        var sourceId = Guid.NewGuid();
        var r = new GoalFundingRule(
            Guid.NewGuid(), goalId, 5_000m, RecurrenceFrequency.Monthly,
            sourceId, new DateOnly(2026, 1, 1), new DateOnly(2030, 12, 31), true);
        await repo.AddAsync(r);

        var loaded = (await repo.GetByGoalAsync(goalId)).Single();
        Assert.Equal(r.Id, loaded.Id);
        Assert.Equal(5_000m, loaded.Amount);
        Assert.Equal(RecurrenceFrequency.Monthly, loaded.Frequency);
        Assert.Equal(sourceId, loaded.SourceCashAccountId);
        Assert.Equal(new DateOnly(2030, 12, 31), loaded.EndDate);
        Assert.True(loaded.IsEnabled);
    }

    [Fact]
    public async Task FundingRule_NullSourceAndEndDate_RoundTrip()
    {
        var repo = new GoalFundingRuleSqliteRepository(_dbPath);
        var goalId = await CreateGoalAsync();
        var r = new GoalFundingRule(
            Guid.NewGuid(), goalId, 1_000m, RecurrenceFrequency.Daily,
            null, new DateOnly(2026, 1, 1), null, true);
        await repo.AddAsync(r);

        var loaded = (await repo.GetByGoalAsync(goalId)).Single();
        Assert.Null(loaded.SourceCashAccountId);
        Assert.Null(loaded.EndDate);
    }

    [Fact]
    public async Task FundingRule_Update_PersistsDisable()
    {
        var repo = new GoalFundingRuleSqliteRepository(_dbPath);
        var goalId = await CreateGoalAsync();
        var r = new GoalFundingRule(
            Guid.NewGuid(), goalId, 1_000m, RecurrenceFrequency.Daily,
            null, new DateOnly(2026, 1, 1), null, true);
        await repo.AddAsync(r);
        await repo.UpdateAsync(r with { IsEnabled = false });

        var loaded = (await repo.GetByGoalAsync(goalId)).Single();
        Assert.False(loaded.IsEnabled);
    }

    [Fact]
    public async Task FundingRule_GetAll_ReturnsAllGoalsRules()
    {
        var repo = new GoalFundingRuleSqliteRepository(_dbPath);
        var g1 = await CreateGoalAsync();
        var g2 = await CreateGoalAsync();
        await repo.AddAsync(new GoalFundingRule(
            Guid.NewGuid(), g1, 1_000m, RecurrenceFrequency.Daily,
            null, new DateOnly(2026, 1, 1), null, true));
        await repo.AddAsync(new GoalFundingRule(
            Guid.NewGuid(), g2, 2_000m, RecurrenceFrequency.Monthly,
            null, new DateOnly(2026, 2, 1), null, true));

        Assert.Equal(2, (await repo.GetAllAsync()).Count);
    }
}
