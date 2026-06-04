using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using System.Globalization;

namespace Assetra.WPF.Features.Goals;

public sealed class GoalFundingRuleRowViewModel
{
    private readonly ICurrencyService? _currency;
    private readonly ILocalizationService? _localization;
    private readonly Func<Guid, string?>? _sourceAccountNameResolver;
    private readonly DateOnly _today;

    public GoalFundingRuleRowViewModel(
        GoalFundingRule rule,
        ICurrencyService? currency = null,
        ILocalizationService? localization = null,
        Func<Guid, string?>? sourceAccountNameResolver = null,
        DateOnly? today = null)
    {
        Rule = rule;
        _currency = currency;
        _localization = localization;
        _sourceAccountNameResolver = sourceAccountNameResolver;
        _today = today ?? DateOnly.FromDateTime(DateTime.Today);
    }

    public GoalFundingRule Rule { get; }

    public string AmountDisplay => FormatAmount(Rule.Amount);
    public string FrequencyDisplay => L($"Recurring.Frequency.{Rule.Frequency}", Rule.Frequency.ToString());
    public string DateRangeDisplay => Rule.EndDate is { } endDate
        ? $"{Rule.StartDate:yyyy-MM-dd} - {endDate:yyyy-MM-dd}"
        : $"{Rule.StartDate:yyyy-MM-dd} -";
    public bool IsEnabled => Rule.IsEnabled;
    public string StatusDisplay => IsEnabled
        ? L("Goals.Detail.Enabled", "Enabled")
        : L("Goals.Detail.Disabled", "Disabled");
    public string SourceAccountDisplay => Rule.SourceCashAccountId is { } sourceAccountId
        ? ResolveSourceAccountDisplay(sourceAccountId)
        : L("Goals.Detail.Funding.Source.NotSet", "No source account");
    public string NextContributionDisplay => ResolveNextContributionDisplay();

    private string FormatAmount(decimal value) =>
        _currency?.FormatAmount(value) ?? $"NT${value:N0}";

    private string ResolveSourceAccountDisplay(Guid sourceAccountId)
    {
        var resolved = _sourceAccountNameResolver?.Invoke(sourceAccountId);
        if (!string.IsNullOrWhiteSpace(resolved))
            return resolved;

        var template = L("Goals.Detail.Funding.Source.Unknown", "Unknown account ({0})");
        return string.Format(CultureInfo.CurrentCulture, template, sourceAccountId.ToString("N")[..8]);
    }

    private string ResolveNextContributionDisplay()
    {
        if (!Rule.IsEnabled)
            return L("Goals.Detail.Funding.Next.Disabled", "Disabled");

        var nextDate = ResolveNextContributionDate();
        if (nextDate is null)
            return L("Goals.Detail.Funding.Next.None", "No upcoming contribution");

        var template = L("Goals.Detail.Funding.Next.Template", "{0} - {1}");
        return string.Format(
            CultureInfo.CurrentCulture,
            template,
            nextDate.Value.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture),
            AmountDisplay);
    }

    private DateOnly? ResolveNextContributionDate()
    {
        if (Rule.EndDate is { } ended && ended < _today)
            return null;

        var next = Rule.StartDate;
        var guard = 0;
        while (next < _today && guard++ < 10_000)
            next = AddFrequency(next);

        if (Rule.EndDate is { } endDate && next > endDate)
            return null;

        return next;
    }

    private DateOnly AddFrequency(DateOnly date) =>
        Rule.Frequency switch
        {
            RecurrenceFrequency.Daily => date.AddDays(1),
            RecurrenceFrequency.Weekly => date.AddDays(7),
            RecurrenceFrequency.BiWeekly => date.AddDays(14),
            RecurrenceFrequency.Monthly => date.AddMonths(1),
            RecurrenceFrequency.Quarterly => date.AddMonths(3),
            RecurrenceFrequency.Yearly => date.AddYears(1),
            _ => date.AddMonths(1),
        };

    private string L(string key, string fallback) =>
        _localization?.Get(key, fallback) ?? fallback;
}
