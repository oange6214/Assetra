namespace Assetra.Core.Models.MonteCarlo;

/// <summary>
/// Monte Carlo 退休模擬輸入參數。報酬率採對數常態分佈（log-normal）模擬。
/// </summary>
/// <param name="InitialBalance">起始投資組合餘額。</param>
/// <param name="AnnualWithdrawal">每年提領金額（以實質購買力計）。</param>
/// <param name="MeanAnnualReturn">年化平均報酬率（已扣通膨）。</param>
/// <param name="AnnualReturnStdDev">年化報酬率標準差。</param>
/// <param name="Years">模擬年數。</param>
/// <param name="SimulationCount">模擬次數（路徑數量）。</param>
/// <param name="RandomSeed">亂數種子（測試用，可選）。</param>
public sealed record MonteCarloInputs(
    decimal InitialBalance,
    decimal AnnualWithdrawal,
    decimal MeanAnnualReturn,
    decimal AnnualReturnStdDev,
    int Years,
    int SimulationCount = 1_000,
    int? RandomSeed = null);
