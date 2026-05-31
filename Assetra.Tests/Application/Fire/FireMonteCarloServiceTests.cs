using Assetra.Application.Fire;
using Assetra.Core.Models.Fire;
using Xunit;

namespace Assetra.Tests.Application.Fire;

public sealed class FireMonteCarloServiceTests
{
    [Fact]
    public void EstimateRetirementSuccess_SameSeed_ReturnsSameSuccessRate()
    {
        var service = new FireMonteCarloService();
        var scenario = CreateScenario(retirementExpenses: 600_000m);

        var first = service.EstimateRetirementSuccess(
            scenario,
            startingBalance: 20_000_000m,
            retirementYears: 40,
            simulationCount: 500,
            randomSeed: 42);
        var second = service.EstimateRetirementSuccess(
            scenario,
            startingBalance: 20_000_000m,
            retirementYears: 40,
            simulationCount: 500,
            randomSeed: 42);

        Assert.Equal(first.SuccessRate, second.SuccessRate);
        Assert.Equal(first.MedianEndingBalance, second.MedianEndingBalance);
    }

    [Fact]
    public void EstimateRetirementSuccess_HigherBalanceAndLowerWithdrawal_IncreasesSuccessRate()
    {
        var service = new FireMonteCarloService();

        var risky = service.EstimateRetirementSuccess(
            CreateScenario(retirementExpenses: 1_200_000m),
            startingBalance: 12_000_000m,
            retirementYears: 45,
            simulationCount: 1_000,
            randomSeed: 7);
        var safer = service.EstimateRetirementSuccess(
            CreateScenario(retirementExpenses: 600_000m),
            startingBalance: 24_000_000m,
            retirementYears: 45,
            simulationCount: 1_000,
            randomSeed: 7);

        Assert.True(safer.SuccessRate > risky.SuccessRate);
    }

    private static FireScenario CreateScenario(decimal retirementExpenses)
    {
        var now = new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero);

        return new FireScenario(
            Id: Guid.NewGuid(),
            Name: "Monte Carlo",
            Mode: FireScenarioMode.Advanced,
            NetWorthSource: FireNetWorthSource.Manual,
            PortfolioGroupId: null,
            CurrentNetWorthOverride: 8_000_000m,
            AnnualExpenses: 600_000m,
            AnnualSavings: 300_000m,
            ExpectedAnnualReturn: 0.05m,
            ReturnMode: FireReturnMode.Real,
            InflationRate: 0.02m,
            SavingsGrowthRate: null,
            ExpenseGrowthRate: null,
            WithdrawalRate: 0.04m,
            CurrentAge: 45,
            LifeExpectancyAge: 90,
            RetirementAnnualExpenses: retirementExpenses,
            CustomTargetAmount: null,
            IncludeTaxes: false,
            Notes: null,
            IsDefault: false,
            CreatedAt: now,
            UpdatedAt: now);
    }
}
