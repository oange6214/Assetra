using Assetra.Core.Interfaces.MonteCarlo;
using Assetra.Core.Models.MonteCarlo;

namespace Assetra.Application.MonteCarlo;

/// <summary>
/// 退休現金流 Monte Carlo 模擬器。
/// 每年報酬以常態分佈 N(μ, σ²) 抽樣，採用 Box-Muller 轉換。
/// 路徑：balance_{t+1} = (balance_t − withdrawal) × (1 + return_t)。
/// 成功 = 模擬期末餘額 ≥ 0 且過程未跌破 0。
/// </summary>
public sealed class MonteCarloSimulator : IMonteCarloSimulator
{
    public MonteCarloResult Simulate(MonteCarloInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Years <= 0)
            throw new ArgumentOutOfRangeException(nameof(inputs.Years), "Years must be positive.");
        if (inputs.SimulationCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(inputs.SimulationCount), "SimulationCount must be positive.");

        var rng = inputs.RandomSeed is int seed ? new Random(seed) : new Random();
        var mean = (double)inputs.MeanAnnualReturn;
        var stdDev = (double)inputs.AnnualReturnStdDev;
        var withdrawal = (double)inputs.AnnualWithdrawal;
        var initial = (double)inputs.InitialBalance;

        // [simIndex][year] : balance path; year 0 = initial.
        var paths = new double[inputs.SimulationCount][];
        int successCount = 0;

        for (int s = 0; s < inputs.SimulationCount; s++)
        {
            var path = new double[inputs.Years + 1];
            path[0] = initial;
            var balance = initial;
            bool depleted = false;

            for (int y = 1; y <= inputs.Years; y++)
            {
                var r = NextNormal(rng, mean, stdDev);
                balance = (balance - withdrawal) * (1.0 + r);
                if (balance < 0)
                {
                    balance = 0;
                    depleted = true;
                }
                path[y] = balance;
            }
            paths[s] = path;
            if (!depleted) successCount++;
        }

        var endings = paths.Select(p => p[inputs.Years]).OrderBy(v => v).ToArray();

        var p10 = Percentile(endings, 0.10);
        var p50 = Percentile(endings, 0.50);
        var p90 = Percentile(endings, 0.90);

        // Median path: per-year median across simulations
        var medianPath = new decimal[inputs.Years + 1];
        var perYear = new double[inputs.SimulationCount];
        for (int y = 0; y <= inputs.Years; y++)
        {
            for (int s = 0; s < inputs.SimulationCount; s++)
                perYear[s] = paths[s][y];
            Array.Sort(perYear);
            medianPath[y] = (decimal)Percentile(perYear, 0.50);
        }

        return new MonteCarloResult(
            SuccessRate: (decimal)successCount / inputs.SimulationCount,
            MedianEndingBalance: (decimal)p50,
            P10EndingBalance: (decimal)p10,
            P90EndingBalance: (decimal)p90,
            MedianBalancePath: medianPath);
    }

    private static double NextNormal(Random rng, double mean, double stdDev)
    {
        // Box-Muller: two uniforms → one standard normal
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return mean + stdDev * z;
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        var idx = (int)Math.Clamp(Math.Round(p * (sorted.Length - 1)), 0, sorted.Length - 1);
        return sorted[idx];
    }
}
