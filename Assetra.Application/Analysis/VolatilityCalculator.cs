using Assetra.Core.Interfaces.Analysis;

namespace Assetra.Application.Analysis;

public sealed class VolatilityCalculator : IVolatilityCalculator
{
    private const double TradingDaysPerYear = 252.0;

    public decimal? ComputeAnnualized(IReadOnlyList<(DateOnly Date, decimal Value)> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count < 2) return null;

        var sorted = values.OrderBy(v => v.Date).ToArray();
        var returns = new List<double>(sorted.Length - 1);
        for (var i = 1; i < sorted.Length; i++)
        {
            var prev = (double)sorted[i - 1].Value;
            var cur = (double)sorted[i].Value;
            if (prev == 0) continue;
            returns.Add(cur / prev - 1.0);
        }
        if (returns.Count < 2) return null;

        var mean = returns.Average();
        var sumSq = returns.Sum(r => (r - mean) * (r - mean));
        var variance = sumSq / (returns.Count - 1);
        var dailyStd = Math.Sqrt(variance);
        return (decimal)(dailyStd * Math.Sqrt(TradingDaysPerYear));
    }
}
