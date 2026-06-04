using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.WPF.Features.Goals;

public sealed class GoalMilestoneRowViewModel
{
    private readonly ICurrencyService? _currency;
    private readonly ILocalizationService? _localization;
    private readonly decimal? _currentProgressAmount;

    public GoalMilestoneRowViewModel(
        GoalMilestone milestone,
        ICurrencyService? currency = null,
        ILocalizationService? localization = null,
        decimal? currentProgressAmount = null)
    {
        Milestone = milestone;
        _currency = currency;
        _localization = localization;
        _currentProgressAmount = currentProgressAmount;
    }

    public GoalMilestone Milestone { get; }

    public string Label => Milestone.Label;
    public string TargetDateDisplay => Milestone.TargetDate.ToString("yyyy-MM-dd");
    public string TargetAmountDisplay => FormatAmount(Milestone.TargetAmount);
    public bool IsAchieved =>
        Milestone.IsAchieved ||
        (_currentProgressAmount is { } currentProgressAmount && currentProgressAmount >= Milestone.TargetAmount);
    public string StatusDisplay => IsAchieved
        ? L("Goals.Detail.Achieved", "Achieved")
        : L("Goals.Detail.Pending", "Pending");

    private string FormatAmount(decimal value) =>
        _currency?.FormatAmount(value) ?? $"NT${value:N0}";

    private string L(string key, string fallback) =>
        _localization?.Get(key, fallback) ?? fallback;
}
