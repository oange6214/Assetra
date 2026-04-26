namespace Assetra.Core.Models;

/// <summary>
/// 財務目標：使用者自訂的儲蓄／淨資產目標。
/// 進度由 <see cref="CurrentAmount"/> / <see cref="TargetAmount"/> 計算。
/// </summary>
public sealed record FinancialGoal(
    Guid Id,
    string Name,
    decimal TargetAmount,
    decimal CurrentAmount,
    DateOnly? Deadline,
    string? Notes)
{
    public decimal ProgressPercent =>
        TargetAmount > 0
            ? Math.Min(CurrentAmount / TargetAmount * 100m, 100m)
            : 0m;

    public bool IsAchieved => TargetAmount > 0 && CurrentAmount >= TargetAmount;

    public decimal Remaining =>
        Math.Max(TargetAmount - CurrentAmount, 0m);

    public int? DaysRemaining =>
        Deadline is { } d ? (d.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber) : null;
}
