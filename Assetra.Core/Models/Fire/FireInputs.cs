namespace Assetra.Core.Models.Fire;

/// <summary>
/// FIRE 計算輸入參數。
/// </summary>
/// <param name="CurrentNetWorth">目前淨資產。</param>
/// <param name="AnnualExpenses">年支出（用於決定 FIRE 目標金額）。</param>
/// <param name="AnnualSavings">年度可投入金額（含儲蓄+投資）。</param>
/// <param name="ExpectedAnnualReturn">預期年化實質報酬率（已扣通膨）。</param>
/// <param name="WithdrawalRate">退休後安全提領率（4% 法則 → 0.04）。</param>
/// <param name="MaxYears">最多模擬年數，避免無解情況下無限延伸。</param>
public sealed record FireInputs(
    decimal CurrentNetWorth,
    decimal AnnualExpenses,
    decimal AnnualSavings,
    decimal ExpectedAnnualReturn,
    decimal WithdrawalRate,
    int MaxYears = 80);
