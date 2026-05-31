using System.Globalization;
using System.IO;
using Assetra.Core.Models.Fire;
using Assetra.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public sealed class FireScenarioSqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"assetra-fire-scenarios-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        { File.Delete(_dbPath); }
        catch { /* best effort */ }
    }

    [Fact]
    public async Task UpsertAsync_PersistsDefaultScenarioAndEventsAfterReopeningDatabase()
    {
        var scenario = Scenario("Default FIRE", isDefault: true);
        var cashFlow = Event(scenario.Id, "租金收入", FireCashFlowDirection.Inflow);

        await using (var repo = new FireScenarioSqliteRepository(_dbPath))
        {
            await repo.UpsertAsync(scenario, [cashFlow]);
        }

        await using var reopened = new FireScenarioSqliteRepository(_dbPath);
        var loaded = await reopened.GetDefaultAsync();
        var events = await reopened.GetCashFlowEventsAsync(scenario.Id);

        Assert.NotNull(loaded);
        Assert.Equal(scenario.Id, loaded.Id);
        Assert.Equal("Default FIRE", loaded.Name);
        Assert.True(loaded.IsDefault);
        Assert.Single(events);
        Assert.Equal("租金收入", events[0].Name);
        Assert.Equal(FireCashFlowDirection.Inflow, events[0].Direction);
    }

    [Fact]
    public async Task UpsertAsync_WhenScenarioIsDefault_ClearsPreviousDefaultScenario()
    {
        await using var repo = new FireScenarioSqliteRepository(_dbPath);
        var first = Scenario("First", isDefault: true);
        var second = Scenario("Second", isDefault: true);

        await repo.UpsertAsync(first, []);
        await repo.UpsertAsync(second, []);

        var scenarios = await repo.GetAllAsync();
        var defaultScenario = await repo.GetDefaultAsync();

        Assert.Equal(second.Id, defaultScenario?.Id);
        Assert.Single(scenarios.Where(x => x.IsDefault));
    }

    [Fact]
    public async Task DeleteAsync_RemovesScenarioCashFlowEvents()
    {
        await using var repo = new FireScenarioSqliteRepository(_dbPath);
        var scenario = Scenario("Delete me", isDefault: true);
        await repo.UpsertAsync(scenario, [Event(scenario.Id, "醫療支出", FireCashFlowDirection.Outflow)]);

        await repo.DeleteAsync(scenario.Id);

        Assert.Null(await repo.GetAsync(scenario.Id));
        Assert.Empty(await repo.GetCashFlowEventsAsync(scenario.Id));
    }

    private static FireScenario Scenario(string name, bool isDefault) =>
        new(
            Guid.NewGuid(),
            name,
            FireScenarioMode.Advanced,
            FireNetWorthSource.Manual,
            PortfolioGroupId: null,
            CurrentNetWorthOverride: 1_000_000m,
            AnnualExpenses: 600_000m,
            AnnualSavings: 300_000m,
            ExpectedAnnualReturn: 0.05m,
            FireReturnMode.Real,
            InflationRate: null,
            SavingsGrowthRate: null,
            ExpenseGrowthRate: null,
            WithdrawalRate: 0.04m,
            CurrentAge: 40,
            LifeExpectancyAge: 90,
            RetirementAnnualExpenses: 600_000m,
            CustomTargetAmount: null,
            IncludeTaxes: false,
            Notes: "test",
            isDefault,
            DateTimeOffset.Parse("2026-05-29T00:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-05-29T00:00:00Z", CultureInfo.InvariantCulture));

    private static FireCashFlowEvent Event(
        Guid scenarioId,
        string name,
        FireCashFlowDirection direction) =>
        new(
            Guid.NewGuid(),
            scenarioId,
            name,
            StartYearOffset: 1,
            EndYearOffset: 3,
            AnnualAmount: 120_000m,
            direction,
            FireCashFlowGrowthMode.Fixed,
            CustomGrowthRate: null,
            Notes: null);
}
