using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Assetra.WPF.Features.Fire;
using Assetra.WPF.Features.PortfolioGroups;

namespace Assetra.WPF.Features.Goals;

/// <summary>
/// 財務目標 MVP：CRUD 一組 <see cref="FinancialGoal"/>，提供進度條清單與新增表單。
/// </summary>
public sealed partial class GoalsViewModel : ObservableObject
{
    private readonly IFinancialGoalRepository _repository;
    private readonly ICurrencyService? _currency;
    private readonly ILocalizationService? _localization;

    /// <summary>
    /// Portfolio-Groups-Refactor P5 — 共用群組目錄，給 dialog 內的 group ComboBox 用。
    /// null = 群組功能未啟用，UI 隱藏 group 選項。
    /// </summary>
    public PortfolioGroupCatalog? GroupCatalog { get; }

    /// <summary>True 當啟用群組功能（catalog 非 null 且有 row）。</summary>
    public bool IsGroupSelectorVisible => GroupCatalog is { Groups.Count: > 0 };

    /// <summary>Add/Edit form 內當前選擇的 group。null = 不連結（沿用 LinkedAssetClass）。</summary>
    [ObservableProperty] private PortfolioGroup? _addPortfolioGroup;

    private readonly ObservableCollection<GoalRowViewModel> _goals = [];
    public ReadOnlyObservableCollection<GoalRowViewModel> Goals { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGoals))]
    [NotifyPropertyChangedFor(nameof(HasNoGoals))]
    private bool _isLoaded;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    // ── In-app confirm dialog (mirrors PortfolioViewModel for consistency) ──
    [ObservableProperty] private bool _isConfirmDialogOpen;
    [ObservableProperty] private string _confirmDialogMessage = string.Empty;
    private Func<Task>? _confirmDialogAction;
    private bool _isFormattingAmountInput;

    [RelayCommand]
    private async Task ConfirmDialogYes()
    {
        IsConfirmDialogOpen = false;
        if (_confirmDialogAction is not null)
            await _confirmDialogAction();
        _confirmDialogAction = null;
    }

    [RelayCommand]
    private void ConfirmDialogNo()
    {
        IsConfirmDialogOpen = false;
        _confirmDialogAction = null;
    }

    private void AskConfirm(string message, Func<Task> action)
    {
        ConfirmDialogMessage = message;
        _confirmDialogAction = action;
        IsConfirmDialogOpen = true;
    }

    // ── Add / Edit form (shared) ──
    [ObservableProperty] private string _addName = string.Empty;
    [ObservableProperty] private string _addTargetAmount = string.Empty;
    [ObservableProperty] private string _addCurrentAmount = string.Empty;
    [ObservableProperty] private DateTime? _addDeadline;
    [ObservableProperty] private string _addNotes = string.Empty;
    [ObservableProperty] private string? _addError;
    [ObservableProperty] private bool _isFormOpen;
    /// <summary>
    /// 2026-05-17：auto-track 目標進度的資產類別 selection。
    /// 空字串 / "Manual" = manual mode（沿用 CurrentAmount）；其他值 = 自動模式。
    /// 對應 <see cref="FinancialGoal.LinkedAssetClass"/>。
    /// </summary>
    [ObservableProperty] private string _addLinkedAssetClass = string.Empty;

    /// <summary>Dropdown 用：(displayKey, value) — value 對應 FinancialGoal.LinkedAssetClass。</summary>
    public IReadOnlyList<(string LabelKey, string Value)> LinkedAssetClassOptions { get; } =
    [
        ("Goals.LinkedAssetClass.Manual",     ""),
        ("Goals.LinkedAssetClass.NetWorth",   "NetWorth"),
        ("Goals.LinkedAssetClass.TotalAssets","TotalAssets"),
        ("Goals.LinkedAssetClass.Investments","Investments"),
        ("Goals.LinkedAssetClass.Cash",       "Cash"),
        ("Goals.LinkedAssetClass.RealEstate", "RealEstate"),
        ("Goals.LinkedAssetClass.Retirement", "Retirement"),
        ("Goals.LinkedAssetClass.Physical",   "Physical"),
    ];

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
    public int GoalCount => Goals.Count;
    public decimal TotalTarget => Goals.Sum(goal => goal.Goal.TargetAmount);
    public decimal TotalCurrent => Goals.Sum(goal => goal.Goal.CurrentAmount);
    public decimal TotalRemaining => Goals.Sum(goal => Math.Max(0m, goal.Goal.Remaining));
    public decimal OverallProgressPercent => TotalTarget <= 0m
        ? 0m
        : Math.Min(100m, TotalCurrent / TotalTarget * 100m);
    public string TotalTargetDisplay => FormatAmount(TotalTarget);
    public string TotalCurrentDisplay => FormatAmount(TotalCurrent);
    public string TotalRemainingDisplay => FormatAmount(TotalRemaining);
    public string OverallProgressDisplay => $"{OverallProgressPercent:F1}%";

    public GoalsViewModel(
        IFinancialGoalRepository repository,
        ICurrencyService? currency = null,
        ILocalizationService? localization = null,
        PortfolioGroupCatalog? groupCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
        _currency = currency;
        _localization = localization;
        GroupCatalog = groupCatalog;
        Goals = new ReadOnlyObservableCollection<GoalRowViewModel>(_goals);
        _goals.CollectionChanged += (_, _) =>
        {
            RefreshGoalSummary();
        };
        if (_currency is not null)
            _currency.CurrencyChanged += OnCurrencyChanged;
        if (_localization is not null)
            _localization.LanguageChanged += OnLanguageChanged;
        WeakReferenceMessenger.Default.Register<FireGoalSavedMessage>(
            this,
            static (recipient, message) => ((GoalsViewModel)recipient).UpsertGoal(message.Value));
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
            _goals.Clear();
            foreach (var g in goals)
                _goals.Add(new GoalRowViewModel(g, _currency, _localization));
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
    private void OpenAddForm()
    {
        ResetAddForm();
        IsFormOpen = true;
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
        if (!TryParseAmount(AddCurrentAmount, out var current) || current < 0m)
        {
            AddError = L("Goals.Error.CurrentAmountInvalid", "Current amount must be 0 or greater");
            return;
        }

        var goal = new FinancialGoal(
            EditingId ?? Guid.NewGuid(),
            AddName.Trim(),
            target,
            current,
            AddDeadline is { } dt ? DateOnly.FromDateTime(dt) : null,
            string.IsNullOrWhiteSpace(AddNotes) ? null : AddNotes.Trim(),
            // Auto-track mode：空字串 / "Manual" 都 normalise 為 null（manual）。
            string.IsNullOrWhiteSpace(AddLinkedAssetClass) ? null : AddLinkedAssetClass,
            // Portfolio-Groups-Refactor P5 — 設定後優先於 LinkedAssetClass。
            AddPortfolioGroup?.Id);

        try
        {
            if (EditingId is { } id)
            {
                await _repository.UpdateAsync(goal).ConfigureAwait(true);
                var existing = Goals.FirstOrDefault(g => g.Id == id);
                if (existing is not null)
                {
                    existing.Goal = goal;
                    RefreshGoalSummary();
                }
            }
            else
            {
                await _repository.AddAsync(goal).ConfigureAwait(true);
                _goals.Add(new GoalRowViewModel(goal, _currency, _localization));
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
        IsFormOpen = true;
        AddName = row.Goal.Name;
        AddTargetAmount = row.Goal.TargetAmount.ToString("0.##", CultureInfo.InvariantCulture);
        AddCurrentAmount = row.Goal.CurrentAmount.ToString("0.##", CultureInfo.InvariantCulture);
        AddDeadline = row.Goal.Deadline is { } d ? d.ToDateTime(TimeOnly.MinValue) : null;
        AddNotes = row.Goal.Notes ?? string.Empty;
        AddLinkedAssetClass = row.Goal.LinkedAssetClass ?? string.Empty;
        // Portfolio-Groups-Refactor P5 — 還原 group 選擇。
        AddPortfolioGroup = GroupCatalog?.FindById(row.Goal.PortfolioGroupId);
    }

    partial void OnAddTargetAmountChanged(string value) =>
        FormatAmountInput(value, formatted => AddTargetAmount = formatted);

    partial void OnAddCurrentAmountChanged(string value) =>
        FormatAmountInput(value, formatted => AddCurrentAmount = formatted);

    [RelayCommand]
    private void CancelEdit() => ResetAddForm();

    [RelayCommand]
    private void Remove(GoalRowViewModel? row)
    {
        if (row is null) return;

        var template = L("Goals.Delete.ConfirmMessage", "Delete \"{0}\"? This cannot be undone.");
        var message = string.Format(CultureInfo.CurrentCulture, template, row.Goal.Name);

        AskConfirm(message, async () =>
        {
            try
            {
                await _repository.RemoveAsync(row.Id).ConfigureAwait(true);
                _goals.Remove(row);
                if (EditingId == row.Id) ResetAddForm();
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
        });
    }

    private void ResetAddForm()
    {
        EditingId = null;
        AddName = string.Empty;
        AddTargetAmount = string.Empty;
        AddCurrentAmount = string.Empty;
        AddDeadline = null;
        AddNotes = string.Empty;
        AddLinkedAssetClass = string.Empty;
        AddPortfolioGroup = null;
        AddError = null;
        IsFormOpen = false;
    }

    /// <summary>
    /// Portfolio-Groups-Refactor P5 — UI 在 dialog 開啟前呼叫，確保 catalog 已載入。
    /// 非同步無回傳；catalog 載入失敗時 ComboBox 維持空，不阻斷 dialog。
    /// </summary>
    public Task EnsureGroupCatalogLoadedAsync() =>
        GroupCatalog?.EnsureLoadedAsync() ?? Task.CompletedTask;

    private void UpsertGoal(FinancialGoal goal)
    {
        if (!IsLoaded)
            return;

        var existing = Goals.FirstOrDefault(g => g.Id == goal.Id);
        if (existing is not null)
        {
            existing.Goal = goal;
            RefreshGoalSummary();
            return;
        }
        _goals.Add(new GoalRowViewModel(goal, _currency, _localization));
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

    private void FormatAmountInput(string value, Action<string> setValue)
    {
        if (_isFormattingAmountInput)
            return;

        var formatted = FormatWithThousands(value);
        if (formatted == value)
            return;

        _isFormattingAmountInput = true;
        try
        {
            setValue(formatted);
        }
        finally
        {
            _isFormattingAmountInput = false;
        }
    }

    private static string FormatWithThousands(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var negative = text.StartsWith('-');
        var body = negative ? text[1..] : text;
        var filtered = new string(body.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray());

        // Bug fix: if the user typed something with no numeric content (e.g.
        // "abc"), don't silently strip it to empty — TryParseAmount treats
        // empty as 0, which let invalid input pass form validation. Return
        // the original so the validator reports it as a parse error.
        if (filtered.Replace(",", "").Length == 0 && body.Length > 0)
            return text;

        body = filtered.Replace(",", "");

        var dotIdx = body.IndexOf('.');
        var intPart = dotIdx >= 0 ? body[..dotIdx] : body;
        var fracPart = dotIdx >= 0 ? body[dotIdx..] : string.Empty;

        if (intPart.Length <= 3)
            return (negative ? "-" : "") + intPart + fracPart;

        var sb = new StringBuilder();
        var offset = intPart.Length % 3;
        if (offset > 0)
            sb.Append(intPart[..offset]);
        for (var i = offset; i < intPart.Length; i += 3)
        {
            if (i > 0)
                sb.Append(',');
            sb.Append(intPart, i, 3);
        }

        return (negative ? "-" : "") + sb + fracPart;
    }

    private void OnCurrencyChanged()
    {
        foreach (var row in Goals)
            row.RefreshDisplayStrings();
        OnPropertyChanged(nameof(TotalTargetDisplay));
        OnPropertyChanged(nameof(TotalCurrentDisplay));
        OnPropertyChanged(nameof(TotalRemainingDisplay));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var row in Goals)
            row.RefreshDisplayStrings();
        OnPropertyChanged(nameof(FormTitle));
        OnPropertyChanged(nameof(SubmitText));
        OnPropertyChanged(nameof(TotalTargetDisplay));
        OnPropertyChanged(nameof(TotalCurrentDisplay));
        OnPropertyChanged(nameof(TotalRemainingDisplay));
    }

    private void RefreshGoalSummary()
    {
        OnPropertyChanged(nameof(HasGoals));
        OnPropertyChanged(nameof(HasNoGoals));
        OnPropertyChanged(nameof(GoalCount));
        OnPropertyChanged(nameof(TotalTarget));
        OnPropertyChanged(nameof(TotalCurrent));
        OnPropertyChanged(nameof(TotalRemaining));
        OnPropertyChanged(nameof(OverallProgressPercent));
        OnPropertyChanged(nameof(TotalTargetDisplay));
        OnPropertyChanged(nameof(TotalCurrentDisplay));
        OnPropertyChanged(nameof(TotalRemainingDisplay));
        OnPropertyChanged(nameof(OverallProgressDisplay));
    }

    private string FormatAmount(decimal value) =>
        _currency?.FormatAmount(value) ?? $"NT${value:N0}";

    private string L(string key, string fallback) =>
        _localization?.Get(key, fallback) ?? fallback;
}
