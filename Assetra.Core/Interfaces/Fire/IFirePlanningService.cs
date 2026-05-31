using Assetra.Core.Models.Fire;

namespace Assetra.Core.Interfaces.Fire;

public interface IFirePlanningService
{
    FirePlanningProjection Project(
        FireScenario scenario,
        IReadOnlyList<FireCashFlowEvent> cashFlowEvents,
        int currentYear,
        int maxYears = 80);
}
