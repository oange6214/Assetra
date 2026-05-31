using Assetra.Application.MonteCarlo;
using Assetra.Core.Interfaces.Fire;
using Assetra.Core.Interfaces.MonteCarlo;
using Assetra.Core.Models.Fire;
using Assetra.Core.Models.MonteCarlo;

namespace Assetra.Application.Fire;

public sealed class FireMonteCarloService : IFireMonteCarloService
{
    private readonly IMonteCarloSimulator _simulator;

    public FireMonteCarloService()
        : this(new MonteCarloSimulator())
    {
    }

    public FireMonteCarloService(IMonteCarloSimulator simulator)
    {
        ArgumentNullException.ThrowIfNull(simulator);
        _simulator = simulator;
    }

    public MonteCarloResult EstimateRetirementSuccess(
        FireScenario scenario,
        decimal startingBalance,
        int retirementYears,
        int simulationCount = 1_000,
        int? randomSeed = null,
        decimal annualReturnStdDev = 0.12m)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        if (startingBalance < 0m)
            throw new ArgumentOutOfRangeException(nameof(startingBalance), "Starting balance cannot be negative.");
        if (retirementYears <= 0)
            throw new ArgumentOutOfRangeException(nameof(retirementYears), "Retirement years must be positive.");

        var withdrawal = scenario.RetirementAnnualExpenses ?? scenario.AnnualExpenses;
        if (withdrawal <= 0m)
            throw new ArgumentOutOfRangeException(nameof(scenario.RetirementAnnualExpenses), "Annual withdrawal must be positive.");

        return _simulator.Simulate(new MonteCarloInputs(
            InitialBalance: startingBalance,
            AnnualWithdrawal: withdrawal,
            MeanAnnualReturn: scenario.ExpectedAnnualReturn,
            AnnualReturnStdDev: annualReturnStdDev,
            Years: retirementYears,
            SimulationCount: simulationCount,
            RandomSeed: randomSeed));
    }
}
