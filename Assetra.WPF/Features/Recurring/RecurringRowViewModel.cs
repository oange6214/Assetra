using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Recurring;

public partial class RecurringRowViewModel : ObservableObject
{
    [ObservableProperty] private Guid _id;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private TradeType _tradeType;
    [ObservableProperty] private decimal _amount;
    [ObservableProperty] private Guid? _cashAccountId;
    [ObservableProperty] private Guid? _categoryId;
    [ObservableProperty] private RecurrenceFrequency _frequency;
    [ObservableProperty] private int _interval;
    [ObservableProperty] private DateTime _startDate;
    [ObservableProperty] private DateTime? _endDate;
    [ObservableProperty] private AutoGenerationMode _generationMode;
    [ObservableProperty] private DateTime? _lastGeneratedAt;
    [ObservableProperty] private DateTime? _nextDueAt;
    [ObservableProperty] private string? _note;
    [ObservableProperty] private bool _isEnabled;

    public string AmountDisplay => $"NT${Amount:N0}";
    public string FrequencyDisplay =>
        ResolveResource($"Recurring.Frequency.{Frequency}", Frequency.ToString());
    public string NextDueDisplay => NextDueAt.HasValue
        ? NextDueAt.Value.ToString("yyyy-MM-dd")
        : "—";
    public string ModeDisplay =>
        ResolveResource($"Recurring.Mode.{GenerationMode}", GenerationMode.ToString());

    /// <summary>
    /// Re-fires PropertyChanged for resource-backed display props so the UI
    /// updates when the active language ResourceDictionary is swapped.
    /// </summary>
    public void RefreshLocalizedDisplay()
    {
        OnPropertyChanged(nameof(FrequencyDisplay));
        OnPropertyChanged(nameof(ModeDisplay));
    }

    private static string ResolveResource(string key, string fallback) =>
        System.Windows.Application.Current?.TryFindResource(key) as string ?? fallback;

    public static RecurringRowViewModel FromModel(RecurringTransaction r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        TradeType = r.TradeType,
        Amount = r.Amount,
        CashAccountId = r.CashAccountId,
        CategoryId = r.CategoryId,
        Frequency = r.Frequency,
        Interval = r.Interval,
        StartDate = r.StartDate,
        EndDate = r.EndDate,
        GenerationMode = r.GenerationMode,
        LastGeneratedAt = r.LastGeneratedAt,
        NextDueAt = r.NextDueAt,
        Note = r.Note,
        IsEnabled = r.IsEnabled,
    };

    public RecurringTransaction ToModel() => new(
        Id, Name, TradeType, Amount, CashAccountId, CategoryId,
        Frequency, Interval, StartDate, EndDate, GenerationMode,
        LastGeneratedAt, NextDueAt, Note, IsEnabled);

    partial void OnAmountChanged(decimal value) => OnPropertyChanged(nameof(AmountDisplay));
    partial void OnFrequencyChanged(RecurrenceFrequency value) => OnPropertyChanged(nameof(FrequencyDisplay));
    partial void OnNextDueAtChanged(DateTime? value) => OnPropertyChanged(nameof(NextDueDisplay));
    partial void OnGenerationModeChanged(AutoGenerationMode value) => OnPropertyChanged(nameof(ModeDisplay));
}
