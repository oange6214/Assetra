using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Goals;

/// <summary>
/// 財務目標 MVP：CRUD 一組 <see cref="FinancialGoal"/>，提供進度條清單與新增表單。
/// </summary>
public sealed partial class GoalsViewModel : ObservableObject
{
    private readonly IFinancialGoalRepository _repository;
    private readonly ICurrencyService? _currency;
    private readonly ILocalizationService? _localization;

    public ObservableCollection<GoalRowViewModel> Goals { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGoals))]
    [NotifyPropertyChangedFor(nameof(HasNoGoals))]
    private bool _isLoaded;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    // ── Add / Edit form (shared) ──
    [ObservableProperty] private string _addName = string.Empty;
    [ObservableProperty] private string _addTargetAmount = string.Empty;
    [ObservableProperty] private string _addCurrentAmount = string.Empty;
    [ObservableProperty] private DateTime? _addDeadline;
    [ObservableProperty] private string _addNotes = string.Empty;
    [ObservableProperty] private string? _addError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(FormTitle))]
    [NotifyPropertyChangedFor(nameof(SubmitText))]
    private Guid? _editingId;

    public bool IsEditing => EditingId.HasValue;
    public string FormTitle => IsEditing
        ? L("Goals.Edit.Title", "Edit goal")
        : L("Goals.Add.Title", "Add goal");
    public string SubmitText => IsEditing
        ? L("Goals.Edit.Submit", "Save")
        : L("Goals.Add.Submit", "Add");

    public bool HasGoals   => Goals.Count > 0;
    public bool HasNoGoals => IsLoaded && Goals.Count == 0;

    public GoalsViewModel(
        IFinancialGoalRepository repository,
        ICurrencyService? currency = null,
        ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
        _currency = currency;
        _localization = localization;
        Goals.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasGoals));
            OnPropertyChanged(nameof(HasNoGoals));
        };
        if (_currency is not null)
            _currency.CurrencyChanged += OnCurrencyChanged;
        if (_localization is not null)
            _localization.LanguageChanged += OnLanguageChanged;
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var goals = await _repository.GetAllAsync().ConfigureAwait(true);
            Goals.Clear();
            foreach (var g in goals)
                Goals.Add(new GoalRowViewModel(g, _currency, _localization));
            IsLoaded = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        AddError = null;
        if (string.IsNullOrWhiteSpace(AddName))
        {
            AddError = L("Goals.Error.NameRequired", "Please enter a name");
            return;
        }
        if (!TryParseAmount(AddTargetAmount, out var target) || target <= 0m)
        {
            AddError = L("Goals.Error.TargetAmountInvalid", "Target amount must be greater than 0");
            return;
        }
        TryParseAmount(AddCurrentAmount, out var current);

        var goal = new FinancialGoal(
            EditingId ?? Guid.NewGuid(),
            AddName.Trim(),
            target,
            current,
            AddDeadline is { } dt ? DateOnly.FromDateTime(dt) : null,
            string.IsNullOrWhiteSpace(AddNotes) ? null : AddNotes.Trim());

        try
        {
            if (EditingId is { } id)
            {
                await _repository.UpdateAsync(goal).ConfigureAwait(true);
                var existing = Goals.FirstOrDefault(g => g.Id == id);
                if (existing is not null)
                    existing.Goal = goal;
            }
            else
            {
                await _repository.AddAsync(goal).ConfigureAwait(true);
                Goals.Add(new GoalRowViewModel(goal, _currency, _localization));
            }
            ResetAddForm();
        }
        catch (Exception ex)
        {
            AddError = ex.Message;
        }
    }

    [RelayCommand]
    private void Edit(GoalRowViewModel? row)
    {
        if (row is null) return;
        AddError = null;
        EditingId = row.Id;
        AddName = row.Goal.Name;
        AddTargetAmount = row.Goal.TargetAmount.ToString("0.##", CultureInfo.InvariantCulture);
        AddCurrentAmount = row.Goal.CurrentAmount.ToString("0.##", CultureInfo.InvariantCulture);
        AddDeadline = row.Goal.Deadline is { } d ? d.ToDateTime(TimeOnly.MinValue) : null;
        AddNotes = row.Goal.Notes ?? string.Empty;
    }

    [RelayCommand]
    private void CancelEdit() => ResetAddForm();

    [RelayCommand]
    private async Task RemoveAsync(GoalRowViewModel? row)
    {
        if (row is null) return;

        var title = L("Goals.Delete.ConfirmTitle", "Delete goal");
        var template = L("Goals.Delete.ConfirmMessage", "Delete \"{0}\"? This cannot be undone.");
        var message = string.Format(CultureInfo.CurrentCulture, template, row.Goal.Name);

        var result = MessageBox.Show(
            message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (result != MessageBoxResult.OK) return;

        try
        {
            await _repository.RemoveAsync(row.Id).ConfigureAwait(true);
            Goals.Remove(row);
            if (EditingId == row.Id) ResetAddForm();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void ResetAddForm()
    {
        EditingId = null;
        AddName = string.Empty;
        AddTargetAmount = string.Empty;
        AddCurrentAmount = string.Empty;
        AddDeadline = null;
        AddNotes = string.Empty;
        AddError = null;
    }

    private static bool TryParseAmount(string? input, out decimal value)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            value = 0m;
            return true;
        }
        return decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
            || decimal.TryParse(input, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
    }

    private void OnCurrencyChanged()
    {
        foreach (var row in Goals)
            row.RefreshDisplayStrings();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var row in Goals)
            row.RefreshDisplayStrings();
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(SubmitText));
    }

    private string L(string key, string fallback) =>
        _localization?.Get(key, fallback) ?? fallback;
}
