namespace Assetra.Core.Models.Fire;

/// <summary>
/// FIRE 計算結果。
/// </summary>
/// <param name="FireNumber">FIRE 目標金額（AnnualExpenses / WithdrawalRate）。</param>
/// <param name="YearsToFire">達成目標所需年數；無解則為 null。</param>
/// <param name="ProjectedNetWorthAtFire">達成 FIRE 時的預期淨資產（≥ FireNumber）；無解則為最後一年餘額。</param>
/// <param name="WealthPath">每年（從第 0 年到結束）淨資產序列。</param>
public sealed record FireProjection(
    decimal FireNumber,
    int? YearsToFire,
    decimal ProjectedNetWorthAtFire,
    IReadOnlyList<decimal> WealthPath);
