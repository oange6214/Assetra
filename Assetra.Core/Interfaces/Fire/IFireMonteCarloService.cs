using Assetra.Core.Models.Fire;
using Assetra.Core.Models.MonteCarlo;

namespace Assetra.Core.Interfaces.Fire;

public interface IFireMonteCarloService
{
    MonteCarloResult EstimateRetirementSuccess(
        FireScenario scenario,
        decimal startingBalance,
        int retirementYears,
        int simulationCount = 1_000,
        int? randomSeed = null,
        decimal annualReturnStdDev = 0.12m);
}
