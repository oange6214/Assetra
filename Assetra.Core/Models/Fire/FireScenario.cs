namespace Assetra.Core.Models.Fire;

public enum FireScenarioMode
{
    Basic = 0,
    Advanced = 1,
}

public enum FireNetWorthSource
{
    Manual = 0,
    AppNetWorth = 1,
    PortfolioGroup = 2,
}

public enum FireReturnMode
{
    Real = 0,
    Nominal = 1,
}

public sealed record FireScenario(
    Guid Id,
    string Name,
    FireScenarioMode Mode,
    FireNetWorthSource NetWorthSource,
    Guid? PortfolioGroupId,
    decimal? CurrentNetWorthOverride,
    decimal AnnualExpenses,
    decimal AnnualSavings,
    decimal ExpectedAnnualReturn,
    FireReturnMode ReturnMode,
    decimal? InflationRate,
    decimal? SavingsGrowthRate,
    decimal? ExpenseGrowthRate,
    decimal WithdrawalRate,
    int? CurrentAge,
    int? LifeExpectancyAge,
    decimal? RetirementAnnualExpenses,
    decimal? CustomTargetAmount,
    bool IncludeTaxes,
    string? Notes,
    bool IsDefault,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
