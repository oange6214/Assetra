using Assetra.Core.Models.Analysis;

namespace Assetra.Core.Interfaces.Analysis;

public interface IXirrCalculator
{
    decimal? Compute(IReadOnlyList<CashFlow> flows, double guess = 0.1);
}
