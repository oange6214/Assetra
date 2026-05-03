using Assetra.Core.Interfaces.MonteCarlo;
using Assetra.Core.Models.MonteCarlo;

namespace Assetra.Application.MonteCarlo;

/// <summary>
/// 退休現金流 Monte Carlo 模擬器。
/// 每年簡單報酬以對數常態分佈抽樣，採用 Box-Muller 轉換產生 log return。
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
        if (inputs.Years > MonteCarloInputs.MaxYears)
            throw new ArgumentOutOfRangeException(
                nameof(inputs.Years),
                $"Years must be <= {MonteCarloInputs.MaxYears}.");
        if (inputs.SimulationCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(inputs.SimulationCount), "SimulationCount must be positive.");
        if (inputs.SimulationCount > MonteCarloInputs.MaxSimulationCount)
            throw new ArgumentOutOfRangeException(
                nameof(inputs.SimulationCount),
                $"SimulationCount must be <= {MonteCarloInputs.MaxSimulationCount}.");
        if (inputs.AnnualReturnStdDev < 0)
            throw new ArgumentOutOfRangeException(nameof(inputs.AnnualReturnStdDev), "AnnualReturnStdDev must be non-negative.");
        if (inputs.MeanAnnualReturn <= -1)
            throw new ArgumentOutOfRangeException(nameof(inputs.MeanAnnualReturn), "MeanAnnualReturn must be greater than -100%.");

        var rng = inputs.RandomSeed is int seed ? new Random(seed) : new Random();
        var mean = (double)inputs.MeanAnnualReturn;
        var stdDev = (double)inputs.AnnualReturnStdDev;
        var (logMean, logStdDev) = ToLogReturnParameters(mean, stdDev);
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
                var r = NextLogNormalSimpleReturn(rng, logMean, logStdDev);
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

    private static double NextLogNormalSimpleReturn(Random rng, double logMean, double logStdDev)
    {
        // Box-Muller: two uniforms -> one standard normal log return.
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        double z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return Math.Exp(logMean + logStdDev * z) - 1.0;
    }

    private static (double Mean, double StdDev) ToLogReturnParameters(double mean, double stdDev)
    {
        if (stdDev == 0)
            return (Math.Log(1.0 + mean), 0.0);

        var onePlusMean = 1.0 + mean;
        var variance = stdDev * stdDev;
        var logVariance = Math.Log(1.0 + variance / (onePlusMean * onePlusMean));
        var logStdDev = Math.Sqrt(logVariance);
        var logMean = Math.Log(onePlusMean) - logVariance / 2.0;
        return (logMean, logStdDev);
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        var idx = (int)Math.Clamp(Math.Round(p * (sorted.Length - 1)), 0, sorted.Length - 1);
        return sorted[idx];
    }
}
