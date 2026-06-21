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

    /// <summary>
    /// 與 <see cref="Compute"/> 同邏輯，但回傳每個 valuation 日的「累積 TWR」序列
    /// （首點 = 0）。末點等於 <see cref="Compute"/>。少於 2 點回 null。
    /// </summary>
    IReadOnlyList<(DateOnly Date, decimal CumulativeTwr)>? ComputeSeries(
        IReadOnlyList<(DateOnly Date, decimal Value)> valuations,
        IReadOnlyList<CashFlow> flows);
}
