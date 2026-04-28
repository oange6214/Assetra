namespace Assetra.Core.Models.MonteCarlo;

/// <summary>
/// Monte Carlo 模擬結果。
/// </summary>
/// <param name="SuccessRate">成功率（餘額在最後一年仍 ≥ 0 的比例）。</param>
/// <param name="MedianEndingBalance">期末餘額中位數。</param>
/// <param name="P10EndingBalance">期末餘額 10 百分位（保守情境）。</param>
/// <param name="P90EndingBalance">期末餘額 90 百分位（樂觀情境）。</param>
/// <param name="MedianBalancePath">中位數情境下每年餘額（從第 0 年到結束）。</param>
public sealed record MonteCarloResult(
    decimal SuccessRate,
    decimal MedianEndingBalance,
    decimal P10EndingBalance,
    decimal P90EndingBalance,
    IReadOnlyList<decimal> MedianBalancePath);
