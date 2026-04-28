using Assetra.Application.MonteCarlo;
using Assetra.Core.Models.MonteCarlo;
using Xunit;

namespace Assetra.Tests.Application.MonteCarlo;

public sealed class MonteCarloSimulatorTests
{
    private static MonteCarloInputs DefaultInputs(int? seed = 42) => new(
        InitialBalance: 1_000_000m,
        AnnualWithdrawal: 40_000m,
        MeanAnnualReturn: 0.05m,
        AnnualReturnStdDev: 0.10m,
        Years: 30,
        SimulationCount: 200,
        RandomSeed: seed);

    [Fact]
    public void Simulate_DeterministicWithSameSeed()
    {
        var sim = new MonteCarloSimulator();
        var a = sim.Simulate(DefaultInputs(seed: 123));
        var b = sim.Simulate(DefaultInputs(seed: 123));

        Assert.Equal(a.SuccessRate, b.SuccessRate);
        Assert.Equal(a.MedianEndingBalance, b.MedianEndingBalance);
        Assert.Equal(a.P10EndingBalance, b.P10EndingBalance);
        Assert.Equal(a.P90EndingBalance, b.P90EndingBalance);
        Assert.Equal(a.MedianBalancePath, b.MedianBalancePath);
    }

    [Fact]
    public void Simulate_SuccessRateIsBoundedZeroToOne()
    {
        var sim = new MonteCarloSimulator();
        var result = sim.Simulate(DefaultInputs());

        Assert.InRange(result.SuccessRate, 0m, 1m);
    }

    [Fact]
    public void Simulate_MedianPathStartsWithInitialAndHasYearsPlusOnePoints()
    {
        var sim = new MonteCarloSimulator();
        var inputs = DefaultInputs();
        var result = sim.Simulate(inputs);

        Assert.Equal(inputs.Years + 1, result.MedianBalancePath.Count);
        Assert.Equal(inputs.InitialBalance, result.MedianBalancePath[0]);
    }

    [Fact]
    public void Simulate_ZeroVolatility_AllPathsAgreeWithDeterministicFormula()
    {
        var sim = new MonteCarloSimulator();
        var inputs = new MonteCarloInputs(
            InitialBalance: 1_000_000m,
            AnnualWithdrawal: 0m,
            MeanAnnualReturn: 0.05m,
            AnnualReturnStdDev: 0m,
            Years: 10,
            SimulationCount: 50,
            RandomSeed: 7);

        var result = sim.Simulate(inputs);

        // No withdrawal + no volatility → balance compounds at mean rate; never depletes.
        Assert.Equal(1m, result.SuccessRate);
        var expectedFinal = 1_000_000d * Math.Pow(1.05, 10);
        Assert.InRange((double)result.MedianEndingBalance, expectedFinal - 1, expectedFinal + 1);
    }

    [Fact]
    public void Simulate_InvalidYears_Throws()
    {
        var sim = new MonteCarloSimulator();
        var inputs = DefaultInputs() with { Years = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Simulate(inputs));
    }

    [Fact]
    public void Simulate_InvalidSimulationCount_Throws()
    {
        var sim = new MonteCarloSimulator();
        var inputs = DefaultInputs() with { SimulationCount = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Simulate(inputs));
    }
}
