using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Assetra.Application.Goals;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Features.Fire;
using Assetra.WPF.Features.PortfolioGroups;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Assetra.WPF.Features.Goals;

/// <summary>
/// 財務目標 MVP：CRUD 一組 <see cref="FinancialGoal"/>，提供進度條清單與新增表單。
/// </summary>
public sealed partial class GoalsViewModel : ObservableObject
{
    private readonly IFinancialGoalRepository _repository;
    private readonly IGoalMilestoneRepository? _milestoneRepository;
    private readonly IGoalFundingRuleRepository? _fundingRuleRepository;
    private readonly IGoalProgressAmountProvider? _progressAmountProvider;
    private readonly ICurrencyService? _currency;
    private readonly ILocalizationService? _localization;

    /// <summary>
    /// Portfolio-Groups-Refactor P5 — 共用群組目錄，給 dialog 內的 group ComboBox 用。
    /// null = 群組功能未啟用，UI 隱藏 group 選項。
    /// </summary>
    public PortfolioGroupCatalog? GroupCatalog { get; }

    /// <summary>
    /// True 當啟用群組功能（catalog 非 null 且至少一個 user-created group）。
    /// P3.9 — 排除 IsSystem default group (見 PortfolioViewModel.HasPortfolioGroups)。
    /// </summary>
    public bool IsGroupSelectorVisible => GroupCatalog?.Groups.Any(g => !g.IsSystem) == true;

    /// <summary>Add/Edit form 內當前選擇的 group。null = 不連結（沿用 LinkedAssetClass）。</summary>
    [ObservableProperty] private PortfolioGroup? _addPortfolioGroup;

    private readonly ObservableCollection<GoalRowViewModel> _goals = [];
    public ReadOnlyObservableCollection<GoalRowViewModel> Goals { get; }
    private readonly ObservableCollection<GoalMilestoneRowViewModel> _selectedMilestones = [];
    public ReadOnlyObservableCollection<GoalMilestoneRowViewModel> SelectedMilestones { get; }
    private readonly ObservableCollection<GoalFundingRuleRowViewModel> _selectedFundingRules = [];
    public ReadOnlyObservableCollection<GoalFundingRuleRowViewModel> SelectedFundingRules { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGoals))]
    [NotifyPropertyChangedFor(nameof(HasNoGoals))]
    private bool _isLoaded;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isGoalDetailLoading;
    [ObservableProperty] private string? _goalDetailError;

    // ── In-app confirm dialog (mirrors PortfolioViewModel for consistency) ──
    [ObservableProperty] private bool _isConfirmDialogOpen;
    [ObservableProperty] private string _confirmDialogMessage = string.Empty;
    private Func<Task>? _confirmDialogAction;
    private bool _isFormattingAmountInput;
    private Guid? _addPortfolioGroupId;
    private Guid? _editingSelectedMilestoneId;
    private Guid? _editingSelectedFundingRuleId;

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

    public bool HasGoals => Goals.Count > 0;
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
    public bool HasSelectedMilestones => SelectedMilestones.Count > 0;
    public bool HasSelectedFundingRules => SelectedFundingRules.Count > 0;
    public IReadOnlyList<RecurrenceFrequency> FundingFrequencyOptions { get; } =
    [
        RecurrenceFrequency.Daily,
        RecurrenceFrequency.Weekly,
        RecurrenceFrequency.BiWeekly,
        RecurrenceFrequency.Monthly,
        RecurrenceFrequency.Quarterly,
        RecurrenceFrequency.Yearly,
    ];
    public string SelectedRequiredMonthlyContributionDisplay =>
        _selectedRequiredMonthlyContribution is { } value ? FormatAmount(value) : "—";
    public string SelectedMonthlyFundingDisplay => FormatAmount(_selectedMonthlyFunding);
    public string SelectedMonthlyFundingGapDisplay =>
        _selectedMonthlyFundingGap is { } value ? FormatAmount(value) : "—";
    public string SelectedProjectedCompletionDisplay =>
        _selectedProjectedCompletionMonths switch
        {
            null => "—",
            0 => L("Goals.Planning.Projected.Achieved", "Achieved"),
            _ => string.Format(
                CultureInfo.CurrentCulture,
                L("Goals.Planning.Projected.Months", "{0} months"),
                _selectedProjectedCompletionMonths.Value),
        };
    public bool HasGoalPlanningWarning => !string.IsNullOrWhiteSpace(GoalPlanningWarning);
    public string SelectedDetailCurrentDisplay => SelectedGoal is null ? "—" : FormatAmount(SelectedDetailCurrentAmount);
    public string SelectedDetailTargetDisplay => SelectedGoal?.TargetDisplay ?? "—";
    public string SelectedDetailRemainingDisplay => SelectedGoal is null
        ? "—"
        : FormatAmount(Math.Max(0m, SelectedGoal.Goal.TargetAmount - SelectedDetailCurrentAmount));
    public string SelectedDetailDeadlineDisplay => SelectedGoal?.DeadlineDisplay ?? "—";
    public decimal SelectedDetailProgressPercent => SelectedGoal?.Goal.TargetAmount > 0m
        ? Math.Min(100m, SelectedDetailCurrentAmount / SelectedGoal.Goal.TargetAmount * 100m)
        : 0m;
    public string SelectedDetailProgressDisplay => $"{SelectedDetailProgressPercent:F1}%";
    public string SelectedDetailNextActionDisplay => BuildSelectedDetailNextAction();
    public bool SelectedDetailHasFireSource => SelectedGoal?.IsFireSynced == true;
    public string SelectedDetailFireRequiredAssetsDisplay => SelectedGoal?.TargetDisplay ?? "—";
    public string SelectedDetailFireScenarioDisplay =>
        ExtractFireScenarioName(SelectedGoal?.Goal.Notes)
        ?? L("Goals.Detail.Fire.Scenario.Calculator", "FIRE calculator");
    public string SelectedDetailFireLastSyncDisplay =>
        L("Goals.Detail.Fire.LastSyncUnavailable", "Not recorded");

    private decimal? _selectedRequiredMonthlyContribution;
    private decimal _selectedMonthlyFunding;
    private decimal? _selectedMonthlyFundingGap;
    private int? _selectedProjectedCompletionMonths;
    private decimal? _selectedPlanningCurrentAmount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGoalDetailOpen))]
    [NotifyPropertyChangedFor(nameof(SelectedDetailHasFireSource))]
    private GoalRowViewModel? _selectedGoal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGoalPlanningWarning))]
    private string? _goalPlanningWarning;

    [ObservableProperty] private string _selectedMilestoneLabel = string.Empty;
    [ObservableProperty] private string _selectedMilestoneTargetAmount = string.Empty;
    [ObservableProperty] private DateTime? _selectedMilestoneTargetDate = DateTime.Today;
    [ObservableProperty] private string _selectedFundingAmount = string.Empty;
    [ObservableProperty] private RecurrenceFrequency _selectedFundingFrequency = RecurrenceFrequency.Monthly;
    [ObservableProperty] private DateTime? _selectedFundingStartDate = DateTime.Today;
    [ObservableProperty] private DateTime? _selectedFundingEndDate;

    public bool IsGoalDetailOpen => SelectedGoal is not null;
    public string SelectedMilestoneSubmitText => _editingSelectedMilestoneId.HasValue
        ? L("Goals.Detail.SaveMilestone", "Save milestone")
        : L("Goals.Detail.AddMilestone", "Add milestone");
    public string SelectedFundingRuleSubmitText => _editingSelectedFundingRuleId.HasValue
        ? L("Goals.Detail.SaveFundingRule", "Save funding rule")
        : L("Goals.Detail.AddFundingRule", "Add funding rule");

    public GoalsViewModel(
        IFinancialGoalRepository repository,
        ICurrencyService? currency = null,
        ILocalizationService? localization = null,
        PortfolioGroupCatalog? groupCatalog = null,
        IGoalMilestoneRepository? milestoneRepository = null,
        IGoalFundingRuleRepository? fundingRuleRepository = null,
        IGoalProgressAmountProvider? progressAmountProvider = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
        _milestoneRepository = milestoneRepository;
        _fundingRuleRepository = fundingRuleRepository;
        _progressAmountProvider = progressAmountProvider;
        _currency = currency;
        _localization = localization;
        GroupCatalog = groupCatalog;
        Goals = new ReadOnlyObservableCollection<GoalRowViewModel>(_goals);
        SelectedMilestones = new ReadOnlyObservableCollection<GoalMilestoneRowViewModel>(_selectedMilestones);
        SelectedFundingRules = new ReadOnlyObservableCollection<GoalFundingRuleRowViewModel>(_selectedFundingRules);
        _goals.CollectionChanged += (_, _) =>
        {
            RefreshGoalSummary();
        };
        _selectedMilestones.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSelectedMilestones));
        _selectedFundingRules.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSelectedFundingRules));
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
        if (IsLoading)
            return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var goals = await _repository.GetAllAsync().ConfigureAwait(true);

            // LoadAsync can run on a background thread: the dashboard prefetches goals via
            // AsyncHelpers.SafeFireAndForget on IPortfolioPositionFeed price ticks, which fire
            // on the quote-fetch worker thread. WPF's CollectionView throws NotSupportedException
            // on cross-thread edits, so marshal the bound-collection mutation to the UI thread
            // (mirrors FinancialOverviewViewModel.RebuildKpiCards). Application.Current is null in
            // headless / unit-test hosts, so we mutate inline there.
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
                await dispatcher.InvokeAsync(() => ApplyLoadedGoals(goals));
            else
                ApplyLoadedGoals(goals);

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

    private void ApplyLoadedGoals(IReadOnlyList<FinancialGoal> goals)
    {
        _goals.Clear();
        foreach (var g in goals)
            _goals.Add(new GoalRowViewModel(g, _currency, _localization));
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
            _addPortfolioGroupId);

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
        if (row is null)
            return;
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
        _addPortfolioGroupId = AddPortfolioGroup?.Id ?? row.Goal.PortfolioGroupId;
    }

    [RelayCommand]
    private async Task OpenGoalDetail(GoalRowViewModel? row)
    {
        if (row is null)
            return;

        SelectedGoal = row;
        await LoadSelectedGoalDetailAsync(row.Id).ConfigureAwait(true);
    }

    [RelayCommand]
    private void CloseGoalDetail()
    {
        SelectedGoal = null;
        ClearSelectedGoalDetail();
    }

    partial void OnAddPortfolioGroupChanged(PortfolioGroup? value) =>
        _addPortfolioGroupId = value?.Id;

    partial void OnAddTargetAmountChanged(string value) =>
        FormatAmountInput(value, formatted => AddTargetAmount = formatted);

    partial void OnAddCurrentAmountChanged(string value) =>
        FormatAmountInput(value, formatted => AddCurrentAmount = formatted);

    partial void OnSelectedMilestoneTargetAmountChanged(string value) =>
        FormatAmountInput(value, formatted => SelectedMilestoneTargetAmount = formatted);

    partial void OnSelectedFundingAmountChanged(string value) =>
        FormatAmountInput(value, formatted => SelectedFundingAmount = formatted);

    partial void OnSelectedGoalChanged(GoalRowViewModel? value)
    {
        AddSelectedMilestoneCommand.NotifyCanExecuteChanged();
        AddSelectedFundingRuleCommand.NotifyCanExecuteChanged();
        NotifySelectedGoalDetailDisplayChanged();
    }

    [RelayCommand]
    private void CancelEdit() => ResetAddForm();

    [RelayCommand(CanExecute = nameof(CanAddSelectedMilestone))]
    private async Task AddSelectedMilestone()
    {
        GoalDetailError = null;
        if (SelectedGoal?.Goal is not { } goal)
        {
            GoalDetailError = L("Goals.Detail.Error.NoGoal", "Select a goal first.");
            return;
        }
        if (_milestoneRepository is null)
        {
            GoalDetailError = L("Goals.Detail.Error.RepositoryUnavailable", "Goal detail storage is unavailable.");
            return;
        }
        if (string.IsNullOrWhiteSpace(SelectedMilestoneLabel))
        {
            GoalDetailError = L("Goals.Detail.Error.MilestoneRequired", "Enter a milestone label.");
            return;
        }
        if (!TryParseAmount(SelectedMilestoneTargetAmount, out var amount) || amount <= 0m)
        {
            GoalDetailError = L("Goals.Detail.Error.MilestoneAmountInvalid", "Milestone amount must be greater than 0.");
            return;
        }
        if (SelectedMilestoneTargetDate is not { } targetDate)
        {
            GoalDetailError = L("Goals.Detail.Error.MilestoneDateRequired", "Select a milestone date.");
            return;
        }

        var editingRow = _editingSelectedMilestoneId is { } editId
            ? _selectedMilestones.FirstOrDefault(row => row.Milestone.Id == editId)
            : null;
        if (_editingSelectedMilestoneId.HasValue && editingRow is null)
        {
            GoalDetailError = L("Goals.Detail.Error.MilestoneMissing", "The selected milestone no longer exists.");
            ResetSelectedMilestoneForm();
            return;
        }

        var milestone = new GoalMilestone(
            _editingSelectedMilestoneId ?? Guid.NewGuid(),
            goal.Id,
            DateOnly.FromDateTime(targetDate),
            amount,
            SelectedMilestoneLabel.Trim(),
            editingRow?.Milestone.IsAchieved ?? false);

        try
        {
            var row = new GoalMilestoneRowViewModel(milestone, _currency, _localization, SelectedDetailCurrentAmount);
            if (editingRow is not null)
            {
                await _milestoneRepository.UpdateAsync(milestone).ConfigureAwait(true);
                _selectedMilestones[_selectedMilestones.IndexOf(editingRow)] = row;
            }
            else
            {
                await _milestoneRepository.AddAsync(milestone).ConfigureAwait(true);
                _selectedMilestones.Add(row);
            }

            ResetSelectedMilestoneForm();
        }
        catch (Exception ex)
        {
            GoalDetailError = ex.Message;
        }
    }

    private bool CanAddSelectedMilestone() =>
        SelectedGoal is not null && _milestoneRepository is not null;

    [RelayCommand]
    private void EditSelectedMilestone(GoalMilestoneRowViewModel? row)
    {
        if (row is null)
            return;

        _editingSelectedMilestoneId = row.Milestone.Id;
        SelectedMilestoneLabel = row.Milestone.Label;
        SelectedMilestoneTargetAmount = row.Milestone.TargetAmount.ToString("N0", CultureInfo.CurrentCulture);
        SelectedMilestoneTargetDate = row.Milestone.TargetDate.ToDateTime(TimeOnly.MinValue);
        OnPropertyChanged(nameof(SelectedMilestoneSubmitText));
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedMilestone))]
    private async Task RemoveSelectedMilestone(GoalMilestoneRowViewModel? row)
    {
        if (row is null || _milestoneRepository is null)
            return;

        GoalDetailError = null;
        try
        {
            await _milestoneRepository.RemoveAsync(row.Milestone.Id).ConfigureAwait(true);
            _selectedMilestones.Remove(row);
            if (_editingSelectedMilestoneId == row.Milestone.Id)
                ResetSelectedMilestoneForm();
        }
        catch (Exception ex)
        {
            GoalDetailError = ex.Message;
        }
    }

    private bool CanRemoveSelectedMilestone(GoalMilestoneRowViewModel? row) =>
        row is not null && _milestoneRepository is not null;

    [RelayCommand(CanExecute = nameof(CanAddSelectedFundingRule))]
    private async Task AddSelectedFundingRule()
    {
        GoalDetailError = null;
        if (SelectedGoal?.Goal is not { } goal)
        {
            GoalDetailError = L("Goals.Detail.Error.NoGoal", "Select a goal first.");
            return;
        }
        if (_fundingRuleRepository is null)
        {
            GoalDetailError = L("Goals.Detail.Error.RepositoryUnavailable", "Goal detail storage is unavailable.");
            return;
        }
        if (!TryParseAmount(SelectedFundingAmount, out var amount) || amount <= 0m)
        {
            GoalDetailError = L("Goals.Detail.Error.FundingAmountInvalid", "Funding amount must be greater than 0.");
            return;
        }
        if (SelectedFundingStartDate is not { } startDateValue)
        {
            GoalDetailError = L("Goals.Detail.Error.FundingStartDateRequired", "Select a funding start date.");
            return;
        }

        var startDate = DateOnly.FromDateTime(startDateValue);
        var endDate = SelectedFundingEndDate is { } endDateValue
            ? DateOnly.FromDateTime(endDateValue)
            : (DateOnly?)null;
        if (endDate is { } end && end < startDate)
        {
            GoalDetailError = L("Goals.Detail.Error.FundingEndDateInvalid", "Funding end date must be after the start date.");
            return;
        }

        var editingRow = _editingSelectedFundingRuleId is { } editId
            ? _selectedFundingRules.FirstOrDefault(row => row.Rule.Id == editId)
            : null;
        if (_editingSelectedFundingRuleId.HasValue && editingRow is null)
        {
            GoalDetailError = L("Goals.Detail.Error.FundingRuleMissing", "The selected funding rule no longer exists.");
            ResetSelectedFundingRuleForm();
            return;
        }

        var rule = new GoalFundingRule(
            _editingSelectedFundingRuleId ?? Guid.NewGuid(),
            goal.Id,
            amount,
            SelectedFundingFrequency,
            editingRow?.Rule.SourceCashAccountId,
            startDate,
            endDate,
            editingRow?.Rule.IsEnabled ?? true);

        try
        {
            var row = new GoalFundingRuleRowViewModel(rule, _currency, _localization);
            if (editingRow is not null)
            {
                await _fundingRuleRepository.UpdateAsync(rule).ConfigureAwait(true);
                _selectedFundingRules[_selectedFundingRules.IndexOf(editingRow)] = row;
            }
            else
            {
                await _fundingRuleRepository.AddAsync(rule).ConfigureAwait(true);
                _selectedFundingRules.Add(row);
            }

            ResetSelectedFundingRuleForm();
            UpdateSelectedGoalPlanning(DateOnly.FromDateTime(DateTime.Today));
        }
        catch (Exception ex)
        {
            GoalDetailError = ex.Message;
        }
    }

    private bool CanAddSelectedFundingRule() =>
        SelectedGoal is not null && _fundingRuleRepository is not null;

    [RelayCommand]
    private void EditSelectedFundingRule(GoalFundingRuleRowViewModel? row)
    {
        if (row is null)
            return;

        _editingSelectedFundingRuleId = row.Rule.Id;
        SelectedFundingAmount = row.Rule.Amount.ToString("N0", CultureInfo.CurrentCulture);
        SelectedFundingFrequency = row.Rule.Frequency;
        SelectedFundingStartDate = row.Rule.StartDate.ToDateTime(TimeOnly.MinValue);
        SelectedFundingEndDate = row.Rule.EndDate?.ToDateTime(TimeOnly.MinValue);
        OnPropertyChanged(nameof(SelectedFundingRuleSubmitText));
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedFundingRule))]
    private async Task RemoveSelectedFundingRule(GoalFundingRuleRowViewModel? row)
    {
        if (row is null || _fundingRuleRepository is null)
            return;

        GoalDetailError = null;
        try
        {
            await _fundingRuleRepository.RemoveAsync(row.Rule.Id).ConfigureAwait(true);
            _selectedFundingRules.Remove(row);
            if (_editingSelectedFundingRuleId == row.Rule.Id)
                ResetSelectedFundingRuleForm();
            UpdateSelectedGoalPlanning(DateOnly.FromDateTime(DateTime.Today));
        }
        catch (Exception ex)
        {
            GoalDetailError = ex.Message;
        }
    }

    private bool CanRemoveSelectedFundingRule(GoalFundingRuleRowViewModel? row) =>
        row is not null && _fundingRuleRepository is not null;

    [RelayCommand]
    private void Remove(GoalRowViewModel? row)
    {
        if (row is null)
            return;

        var template = L("Goals.Delete.ConfirmMessage", "Delete \"{0}\"? This cannot be undone.");
        var message = string.Format(CultureInfo.CurrentCulture, template, row.Goal.Name);

        AskConfirm(message, async () =>
        {
            try
            {
                await _repository.RemoveAsync(row.Id).ConfigureAwait(true);
                _goals.Remove(row);
                if (SelectedGoal?.Id == row.Id)
                    CloseGoalDetail();
                if (EditingId == row.Id)
                    ResetAddForm();
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
        _addPortfolioGroupId = null;
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

    private async Task LoadSelectedGoalDetailAsync(Guid goalId)
    {
        ClearSelectedGoalDetail();
        GoalDetailError = null;

        IsGoalDetailLoading = true;
        try
        {
            if (SelectedGoal?.Goal is { } goal)
                _selectedPlanningCurrentAmount = await ResolvePlanningCurrentAmountAsync(goal).ConfigureAwait(true);

            if (_milestoneRepository is not null)
            {
                var milestones = await _milestoneRepository.GetByGoalAsync(goalId).ConfigureAwait(true);
                foreach (var milestone in milestones)
                    _selectedMilestones.Add(new GoalMilestoneRowViewModel(milestone, _currency, _localization, SelectedDetailCurrentAmount));
            }

            if (_fundingRuleRepository is not null)
            {
                var fundingRules = await _fundingRuleRepository.GetByGoalAsync(goalId).ConfigureAwait(true);
                foreach (var rule in fundingRules)
                    _selectedFundingRules.Add(new GoalFundingRuleRowViewModel(rule, _currency, _localization));
            }

            UpdateSelectedGoalPlanning(DateOnly.FromDateTime(DateTime.Today));
        }
        catch (Exception ex)
        {
            GoalDetailError = ex.Message;
        }
        finally
        {
            IsGoalDetailLoading = false;
        }
    }

    private void ClearSelectedGoalDetail()
    {
        _selectedMilestones.Clear();
        _selectedFundingRules.Clear();
        ResetSelectedMilestoneForm();
        ResetSelectedFundingRuleForm();
        _selectedPlanningCurrentAmount = null;
        GoalDetailError = null;
        ClearSelectedGoalPlanning();
        IsGoalDetailLoading = false;
    }

    private void ResetSelectedMilestoneForm()
    {
        _editingSelectedMilestoneId = null;
        SelectedMilestoneLabel = string.Empty;
        SelectedMilestoneTargetAmount = string.Empty;
        SelectedMilestoneTargetDate = DateTime.Today;
        OnPropertyChanged(nameof(SelectedMilestoneSubmitText));
    }

    private void ResetSelectedFundingRuleForm()
    {
        _editingSelectedFundingRuleId = null;
        SelectedFundingAmount = string.Empty;
        SelectedFundingFrequency = RecurrenceFrequency.Monthly;
        SelectedFundingStartDate = DateTime.Today;
        SelectedFundingEndDate = null;
        OnPropertyChanged(nameof(SelectedFundingRuleSubmitText));
    }

    private async Task<decimal> ResolvePlanningCurrentAmountAsync(FinancialGoal goal)
    {
        if (!goal.IsAutoTracked || _progressAmountProvider is null)
            return goal.CurrentAmount;

        var resolved = await _progressAmountProvider.GetCurrentAmountAsync(goal).ConfigureAwait(true);
        return resolved ?? goal.CurrentAmount;
    }

    private void UpdateSelectedGoalPlanning(DateOnly today)
    {
        ClearSelectedGoalPlanning();

        if (SelectedGoal?.Goal is not { } goal)
            return;

        var currentAmount = _selectedPlanningCurrentAmount ?? goal.CurrentAmount;
        _selectedMonthlyFunding = CalculateMonthlyFunding(today);
        if (goal.TargetAmount <= 0m)
        {
            GoalPlanningWarning = L("Goals.Planning.Warning.ZeroTarget", "Target amount is not set.");
            NotifyGoalPlanningDisplayChanged();
            return;
        }

        _selectedProjectedCompletionMonths = GoalPlanningService.MonthsToReachTarget(
            currentAmount,
            goal.TargetAmount,
            annualReturnRate: 0m,
            monthlyContribution: _selectedMonthlyFunding);

        if (currentAmount >= goal.TargetAmount)
        {
            _selectedRequiredMonthlyContribution = 0m;
            _selectedMonthlyFundingGap = 0m;
            GoalPlanningWarning = L("Goals.Planning.Warning.Achieved", "This goal is already reached.");
            NotifyGoalPlanningDisplayChanged();
            return;
        }

        if (goal.Deadline is not { } deadline)
        {
            GoalPlanningWarning = L("Goals.Planning.Warning.NoDeadline", "Set a deadline to calculate the required monthly contribution.");
            NotifyGoalPlanningDisplayChanged();
            return;
        }

        var months = MonthsUntilDeadline(today, deadline);
        var requiredMonthly = GoalPlanningService.RequiredMonthlyContribution(
            currentAmount,
            goal.TargetAmount,
            annualReturnRate: 0m,
            months);

        _selectedRequiredMonthlyContribution = requiredMonthly;
        _selectedMonthlyFundingGap = requiredMonthly is { } required
            ? Math.Max(required - _selectedMonthlyFunding, 0m)
            : null;
        if (requiredMonthly is null)
            GoalPlanningWarning = L("Goals.Planning.Warning.DeadlinePassed", "The deadline has passed before this goal was reached.");

        NotifyGoalPlanningDisplayChanged();
    }

    private decimal CalculateMonthlyFunding(DateOnly today) =>
        SelectedFundingRules
            .Select(row => row.Rule)
            .Where(rule => rule.IsEnabled && (rule.EndDate is null || rule.EndDate >= today))
            .Sum(rule => MonthlyEquivalent(rule.Amount, rule.Frequency));

    private static decimal MonthlyEquivalent(decimal amount, RecurrenceFrequency frequency) =>
        frequency switch
        {
            RecurrenceFrequency.Daily => amount * 365.2425m / 12m,
            RecurrenceFrequency.Weekly => amount * 52.1429m / 12m,
            RecurrenceFrequency.BiWeekly => amount * 26.0714m / 12m,
            RecurrenceFrequency.Monthly => amount,
            RecurrenceFrequency.Quarterly => amount / 3m,
            RecurrenceFrequency.Yearly => amount / 12m,
            _ => amount,
        };

    private static int MonthsUntilDeadline(DateOnly today, DateOnly deadline)
    {
        if (deadline < today)
            return 0;
        return (deadline.Year - today.Year) * 12 + deadline.Month - today.Month + 1;
    }

    private void ClearSelectedGoalPlanning()
    {
        _selectedRequiredMonthlyContribution = null;
        _selectedMonthlyFunding = 0m;
        _selectedMonthlyFundingGap = null;
        _selectedProjectedCompletionMonths = null;
        GoalPlanningWarning = null;
        NotifyGoalPlanningDisplayChanged();
    }

    private void NotifyGoalPlanningDisplayChanged()
    {
        OnPropertyChanged(nameof(SelectedRequiredMonthlyContributionDisplay));
        OnPropertyChanged(nameof(SelectedMonthlyFundingDisplay));
        OnPropertyChanged(nameof(SelectedMonthlyFundingGapDisplay));
        OnPropertyChanged(nameof(SelectedProjectedCompletionDisplay));
        NotifySelectedGoalDetailDisplayChanged();
    }

    private decimal SelectedDetailCurrentAmount =>
        _selectedPlanningCurrentAmount ?? SelectedGoal?.Goal.CurrentAmount ?? 0m;

    private string BuildSelectedDetailNextAction()
    {
        if (SelectedGoal?.Goal is not { } goal)
            return "—";

        var currentAmount = SelectedDetailCurrentAmount;
        if (goal.TargetAmount <= 0m)
            return L("Goals.Detail.NextAction.SetTarget", "Set a target amount to get a useful next step.");
        if (currentAmount >= goal.TargetAmount)
            return L("Goals.Detail.NextAction.Achieved", "Goal reached. Review or archive it when you are done.");
        if (goal.Deadline is null)
            return L("Goals.Detail.NextAction.SetDeadline", "Set a deadline so Assetra can calculate the monthly contribution.");
        if (_selectedRequiredMonthlyContribution is null)
            return L("Goals.Detail.NextAction.DeadlinePassed", "Update the deadline or increase funding to recover this goal.");
        if (_selectedMonthlyFunding <= 0m && _selectedRequiredMonthlyContribution > 0m)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                L("Goals.Detail.NextAction.CreateFundingRule", "Create a funding rule of {0}/month."),
                FormatAmount(_selectedRequiredMonthlyContribution.Value));
        }
        if (_selectedMonthlyFundingGap is > 0m)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                L("Goals.Detail.NextAction.AddMonthly", "Add {0}/month to stay on target."),
                FormatAmount(_selectedMonthlyFundingGap.Value));
        }

        return L("Goals.Detail.NextAction.OnTrack", "Current funding is on track for the deadline.");
    }

    private void NotifySelectedGoalDetailDisplayChanged()
    {
        OnPropertyChanged(nameof(SelectedDetailCurrentDisplay));
        OnPropertyChanged(nameof(SelectedDetailTargetDisplay));
        OnPropertyChanged(nameof(SelectedDetailRemainingDisplay));
        OnPropertyChanged(nameof(SelectedDetailDeadlineDisplay));
        OnPropertyChanged(nameof(SelectedDetailProgressPercent));
        OnPropertyChanged(nameof(SelectedDetailProgressDisplay));
        OnPropertyChanged(nameof(SelectedDetailNextActionDisplay));
        OnPropertyChanged(nameof(SelectedDetailHasFireSource));
        OnPropertyChanged(nameof(SelectedDetailFireRequiredAssetsDisplay));
        OnPropertyChanged(nameof(SelectedDetailFireScenarioDisplay));
        OnPropertyChanged(nameof(SelectedDetailFireLastSyncDisplay));
    }

    private static string? ExtractFireScenarioName(string? notes)
    {
        const string prefix = "Generated from FIRE scenario \"";
        if (string.IsNullOrWhiteSpace(notes))
            return null;

        var trimmed = notes.Trim();
        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var start = prefix.Length;
        var end = trimmed.IndexOf('"', start);
        return end > start ? trimmed[start..end] : null;
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
        NotifyGoalPlanningDisplayChanged();
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
        NotifyGoalPlanningDisplayChanged();
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
