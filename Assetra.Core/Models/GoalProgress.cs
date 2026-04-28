namespace Assetra.Core.Models;

/// <summary>
/// 目標進度快照：給定 asOf 日期，依 funding rule 歷史 + 目前資產淨值計算總進度。
/// </summary>
public sealed record GoalProgress(
    Guid GoalId,
    decimal TargetAmount,
    decimal AccumulatedFunding,
    decimal CurrentAmount,
    decimal ProgressPercent,
    bool IsAchieved);
