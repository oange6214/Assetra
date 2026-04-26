using System.Collections.ObjectModel;
using System.Globalization;
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

    public ObservableCollection<GoalRowViewModel> Goals { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGoals))]
    [NotifyPropertyChangedFor(nameof(HasNoGoals))]
    private bool _isLoaded;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    // ── Add form ──
    [ObservableProperty] private string _addName = string.Empty;
    [ObservableProperty] private string _addTargetAmount = string.Empty;
    [ObservableProperty] private string _addCurrentAmount = string.Empty;
    [ObservableProperty] private DateTime? _addDeadline;
    [ObservableProperty] private string _addNotes = string.Empty;
    [ObservableProperty] private string? _addError;

    public bool HasGoals   => Goals.Count > 0;
    public bool HasNoGoals => IsLoaded && Goals.Count == 0;

    public GoalsViewModel(IFinancialGoalRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
        Goals.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasGoals));
            OnPropertyChanged(nameof(HasNoGoals));
        };
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
                Goals.Add(new GoalRowViewModel(g));
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
            AddError = "Name is required";
            return;
        }
        if (!TryParseAmount(AddTargetAmount, out var target) || target <= 0m)
        {
            AddError = "Target amount must be > 0";
            return;
        }
        TryParseAmount(AddCurrentAmount, out var current);

        var goal = new FinancialGoal(
            Guid.NewGuid(),
            AddName.Trim(),
            target,
            current,
            AddDeadline is { } dt ? DateOnly.FromDateTime(dt) : null,
            string.IsNullOrWhiteSpace(AddNotes) ? null : AddNotes.Trim());

        try
        {
            await _repository.AddAsync(goal).ConfigureAwait(true);
            Goals.Add(new GoalRowViewModel(goal));
            ResetAddForm();
        }
        catch (Exception ex)
        {
            AddError = ex.Message;
        }
    }

    [RelayCommand]
    private async Task RemoveAsync(GoalRowViewModel? row)
    {
        if (row is null) return;
        try
        {
            await _repository.RemoveAsync(row.Id).ConfigureAwait(true);
            Goals.Remove(row);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void ResetAddForm()
    {
        AddName = string.Empty;
        AddTargetAmount = string.Empty;
        AddCurrentAmount = string.Empty;
        AddDeadline = null;
        AddNotes = string.Empty;
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
}
