using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models.Analysis;

namespace Assetra.Application.Analysis;

public sealed class TimeWeightedReturnCalculator : ITimeWeightedReturnCalculator
{
    public decimal? Compute(
        IReadOnlyList<(DateOnly Date, decimal Value)> valuations,
        IReadOnlyList<CashFlow> flows)
    {
        ArgumentNullException.ThrowIfNull(valuations);
        ArgumentNullException.ThrowIfNull(flows);
        if (valuations.Count < 2) return null;

        var v = valuations.OrderBy(x => x.Date).ToArray();
        var flowByDate = flows
            .GroupBy(f => f.Date)
            .ToDictionary(g => g.Key, g => g.Sum(f => f.Amount));

        var compound = 1m;
        for (var i = 1; i < v.Length; i++)
        {
            var startV = v[i - 1].Value;
            var endV = v[i].Value;
            // Flow occurring on segment end date is treated as end-of-day (subtracted from end value).
            flowByDate.TryGetValue(v[i].Date, out var flow);
            if (startV == 0) continue;
            var segReturn = (endV - flow - startV) / startV;
            compound *= 1m + segReturn;
        }
        return compound - 1m;
    }
}
