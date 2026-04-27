using Assetra.Core.Models.Analysis;

namespace Assetra.Core.Interfaces.Analysis;

public interface ITimeWeightedReturnCalculator
{
    /// <summary>
    /// Computes TWR by chaining sub-period returns split at each external cash flow.
    /// </summary>
    /// <param name="valuations">Portfolio market value at each timestamp; must be sorted ascending and include endpoints.</param>
    /// <param name="flows">External cash flows (deposits +, withdrawals −) on the dates they occurred.</param>
    decimal? Compute(
        IReadOnlyList<(DateOnly Date, decimal Value)> valuations,
        IReadOnlyList<CashFlow> flows);
}
