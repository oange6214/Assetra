using Assetra.Application.Fire;
using Assetra.Core.Models.Fire;
using Xunit;

namespace Assetra.Tests.Application.Fire;

public sealed class FirePlanningServiceTests
{
    [Fact]
    public void Project_BasicEquivalentScenario_MatchesRequiredAssetsFormula()
    {
        var service = new FirePlanningService();
        var scenario = CreateScenario(
            currentNetWorth: 1_000_000m,
            annualExpenses: 600_000m,
            annualSavings: 300_000m,
            expectedAnnualReturn: 0.05m,
            withdrawalRate: 0.04m);

        var result = service.Project(scenario, Array.Empty<FireCashFlowEvent>(), currentYear: 2026);

        Assert.Equal(15_000_000m, result.RequiredAssets);
        Assert.NotEmpty(result.AccumulationPath);
        Assert.Equal(1_000_000m, result.AccumulationPath[0].NetWorth);
        Assert.NotNull(result.YearsToFire);
        Assert.Equal(2026 + result.YearsToFire.Value, result.FireYear);
    }

    [Fact]
    public void Project_NominalModeWithoutInflation_ReturnsWarning()
    {
        var service = new FirePlanningService();
        var scenario = CreateScenario(
            returnMode: FireReturnMode.Nominal,
            inflationRate: null);

        var result = service.Project(scenario, Array.Empty<FireCashFlowEvent>(), currentYear: 2026);

        Assert.Contains(
            result.Warnings,
            warning => warning.Code == FireProjectionWarningCode.InflationMissingForNominalMode);
    }

    private static FireScenario CreateScenario(
        decimal currentNetWorth = 1_000_000m,
        decimal annualExpenses = 600_000m,
        decimal annualSavings = 300_000m,
        decimal expectedAnnualReturn = 0.05m,
        decimal withdrawalRate = 0.04m,
        FireScenarioMode mode = FireScenarioMode.Basic,
        FireReturnMode returnMode = FireReturnMode.Real,
        decimal? inflationRate = null)
    {
        var now = new DateTimeOffset(2026, 5, 29, 0, 0, 0, TimeSpan.Zero);

        return new FireScenario(
            Id: Guid.NewGuid(),
            Name: "Base",
            Mode: mode,
            NetWorthSource: FireNetWorthSource.Manual,
            PortfolioGroupId: null,
            CurrentNetWorthOverride: currentNetWorth,
            AnnualExpenses: annualExpenses,
            AnnualSavings: annualSavings,
            ExpectedAnnualReturn: expectedAnnualReturn,
            ReturnMode: returnMode,
            InflationRate: inflationRate,
            SavingsGrowthRate: null,
            ExpenseGrowthRate: null,
            WithdrawalRate: withdrawalRate,
            CurrentAge: null,
            LifeExpectancyAge: null,
            RetirementAnnualExpenses: null,
            CustomTargetAmount: null,
            IncludeTaxes: false,
            Notes: null,
            IsDefault: true,
            CreatedAt: now,
            UpdatedAt: now);
    }
}
