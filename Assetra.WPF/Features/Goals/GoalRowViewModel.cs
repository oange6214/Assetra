using Assetra.Core.Models;
using Assetra.Core.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Goals;

/// <summary>
/// 單一財務目標的列檢視模型，包裝 <see cref="FinancialGoal"/> 並提供顯示字串。
/// </summary>
public sealed partial class GoalRowViewModel : ObservableObject
{
    private readonly ICurrencyService? _currency;
    private readonly ILocalizationService? _localization;

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

    public GoalRowViewModel(
        FinancialGoal goal,
        ICurrencyService? currency = null,
        ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(goal);
        _goal = goal;
        _currency = currency;
        _localization = localization;
    }

    public Guid Id            => Goal.Id;
    public string Name        => Goal.Name;
    public string? Notes      => Goal.Notes;

    public string TargetDisplay    => FormatAmount(Goal.TargetAmount);
    public string CurrentDisplay   => FormatAmount(Goal.CurrentAmount);
    public string RemainingDisplay => FormatAmount(Goal.Remaining);

    public decimal ProgressPercent => Goal.ProgressPercent;
    public string  ProgressDisplay => $"{ProgressPercent:F1}%";

    public string DeadlineDisplay
    {
        get
        {
            if (Goal.Deadline is not { } d) return "—";
            var days = Goal.DaysRemaining ?? 0;
            return days >= 0
                ? $"{d:yyyy-MM-dd} ({FormatDaysRemaining(days)})"
                : $"{d:yyyy-MM-dd} ({L("Goals.Deadline.Overdue", "Overdue")})";
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

    public void RefreshDisplayStrings()
    {
        OnPropertyChanged(nameof(TargetDisplay));
        OnPropertyChanged(nameof(CurrentDisplay));
        OnPropertyChanged(nameof(RemainingDisplay));
        OnPropertyChanged(nameof(DeadlineDisplay));
    }

    private string FormatAmount(decimal value) =>
        _currency?.FormatAmount(value) ?? $"NT${value:N0}";

    private string FormatDaysRemaining(int days)
    {
        var template = L("Goals.Deadline.DaysRemaining", "{0}d");
        return string.Format(template, days);
    }

    private string L(string key, string fallback) =>
        _localization?.Get(key, fallback) ?? fallback;
}
