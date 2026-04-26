using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Goals;

/// <summary>
/// 單一財務目標的列檢視模型，包裝 <see cref="FinancialGoal"/> 並提供顯示字串。
/// </summary>
public sealed partial class GoalRowViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    [NotifyPropertyChangedFor(nameof(TargetDisplay))]
    [NotifyPropertyChangedFor(nameof(CurrentDisplay))]
    [NotifyPropertyChangedFor(nameof(RemainingDisplay))]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    [NotifyPropertyChangedFor(nameof(ProgressDisplay))]
    [NotifyPropertyChangedFor(nameof(DeadlineDisplay))]
    [NotifyPropertyChangedFor(nameof(IsAchieved))]
    [NotifyPropertyChangedFor(nameof(StatusTag))]
    private FinancialGoal _goal;

    public GoalRowViewModel(FinancialGoal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        _goal = goal;
    }

    public Guid Id            => Goal.Id;
    public string Name        => Goal.Name;
    public string? Notes      => Goal.Notes;

    public string TargetDisplay    => $"NT${Goal.TargetAmount:N0}";
    public string CurrentDisplay   => $"NT${Goal.CurrentAmount:N0}";
    public string RemainingDisplay => $"NT${Goal.Remaining:N0}";

    public decimal ProgressPercent => Goal.ProgressPercent;
    public string  ProgressDisplay => $"{ProgressPercent:F1}%";

    public string DeadlineDisplay
    {
        get
        {
            if (Goal.Deadline is not { } d) return "—";
            var days = Goal.DaysRemaining ?? 0;
            return days >= 0
                ? $"{d:yyyy-MM-dd} ({days}d)"
                : $"{d:yyyy-MM-dd} (overdue)";
        }
    }

    public bool   IsAchieved => Goal.IsAchieved;

    /// <summary>"achieved" | "ontrack" | "warning" | "overdue" — XAML triggers.</summary>
    public string StatusTag
    {
        get
        {
            if (IsAchieved) return "achieved";
            if (Goal.DaysRemaining is { } d)
            {
                if (d < 0) return "overdue";
                if (d <= 30 && ProgressPercent < 80m) return "warning";
            }
            return "ontrack";
        }
    }
}
