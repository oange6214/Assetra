namespace Assetra.Core.Models;

/// <summary>
/// 子目標里程碑：附屬於 <see cref="FinancialGoal"/>，代表通往最終目標路上的中繼點。
/// 例：總目標 NT$10M，可拆 NT$2M / NT$5M / NT$8M 三個 milestone。
/// </summary>
public sealed record GoalMilestone(
    Guid Id,
    Guid GoalId,
    DateOnly TargetDate,
    decimal TargetAmount,
    string Label,
    bool IsAchieved);
