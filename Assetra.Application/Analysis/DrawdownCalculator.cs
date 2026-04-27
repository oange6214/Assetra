using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models.Analysis;

namespace Assetra.Application.Analysis;

public sealed class DrawdownCalculator : IDrawdownCalculator
{
    public IReadOnlyList<DrawdownPoint> Compute(IReadOnlyList<(DateOnly Date, decimal Value)> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0) return Array.Empty<DrawdownPoint>();

        var sorted = values.OrderBy(v => v.Date).ToArray();
        var points = new List<DrawdownPoint>(sorted.Length);
        var peak = sorted[0].Value;
        foreach (var (date, value) in sorted)
        {
            if (value > peak) peak = value;
            var dd = peak == 0 ? 0m : (peak - value) / peak;
            points.Add(new DrawdownPoint(date, value, peak, dd));
        }
        return points;
    }

    public decimal? ComputeMaxDrawdown(IReadOnlyList<(DateOnly Date, decimal Value)> values)
    {
        var points = Compute(values);
        return points.Count == 0 ? null : points.Max(p => p.Drawdown);
    }
}
