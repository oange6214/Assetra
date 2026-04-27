using Assetra.Core.Models.Analysis;

namespace Assetra.Core.Interfaces.Analysis;

public interface IDrawdownCalculator
{
    IReadOnlyList<DrawdownPoint> Compute(IReadOnlyList<(DateOnly Date, decimal Value)> values);
    decimal? ComputeMaxDrawdown(IReadOnlyList<(DateOnly Date, decimal Value)> values);
}
