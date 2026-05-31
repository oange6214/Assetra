namespace Assetra.Core.Models.Fire;

public sealed record FirePlanningInputs(
    FireScenario Scenario,
    decimal CurrentNetWorth,
    IReadOnlyList<FireCashFlowEvent> CashFlowEvents,
    int CurrentYear,
    int MaxYears = 80);
