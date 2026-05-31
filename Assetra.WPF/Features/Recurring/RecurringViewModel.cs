using System.Collections.ObjectModel;
using Assetra.Application.Recurring.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Recurring;

public partial class RecurringViewModel : ObservableObject
{
    private readonly IRecurringTransactionRepository _recurringRepo;
    private readonly IPendingRecurringEntryRepository _pendingRepo;
    private readonly RecurringTransactionScheduler _scheduler;
    private readonly ISnackbarService _snackbar;
    private readonly ILocalizationService _localization;

    private readonly ObservableCollection<RecurringRowViewModel> _subscriptions = [];
    private readonly ObservableCollection<PendingRecurringRowViewModel> _pending = [];
    public ReadOnlyObservableCollection<RecurringRowViewModel> Subscriptions { get; }
    public ReadOnlyObservableCollection<PendingRecurringRowViewModel> Pending { get; }

    public int SubscriptionCount => _subscriptions.Count;
    public int EnabledSubscriptionCount => _subscriptions.Count(row => row.IsEnabled);
    public int DisabledSubscriptionCount => _subscriptions.Count(row => !row.IsEnabled);
    public int PendingCount => _pending.Count;
    public bool HasPending => _pending.Count > 0;
    public bool HasNoPending => _pending.Count == 0;
    public string PendingBadge => _pending.Count > 99 ? "99+" : _pending.Count.ToString();

    [ObservableProperty] private bool _isLoaded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunSchedulerCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _runStatus = string.Empty;
    [ObservableProperty] private bool _isAddFormOpen;
    [ObservableProperty] private bool _isDeleteConfirmOpen;
    [ObservableProperty] private string _deleteTargetName = string.Empty;
    private RecurringRowViewModel? _pendingDelete;
    public bool HasNoSubscriptions => _subscriptions.Count == 0;
    public bool HasSubscriptions => _subscriptions.Count > 0;

    // Add subscription form
    [ObservableProperty] private string _addName = string.Empty;
    [ObservableProperty] private TradeType _addTradeType = TradeType.Withdrawal;
    [ObservableProperty] private decimal _addAmount;
    [ObservableProperty] private RecurrenceFrequency _addFrequency = RecurrenceFrequency.Monthly;
    [ObservableProperty] private int _addInterval = 1;
    [ObservableProperty] private DateTime _addStartDate = DateTime.Today;
    [ObservableProperty] private AutoGenerationMode _addGenerationMode = AutoGenerationMode.PendingConfirm;
    [ObservableProperty] private string _addNote = string.Empty;
    [ObservableProperty] private string _addError = string.Empty;

    public IReadOnlyList<RecurrenceFrequency> FrequencyOptions { get; } =
        [RecurrenceFrequency.Daily, RecurrenceFrequency.Weekly, RecurrenceFrequency.BiWeekly,
         RecurrenceFrequency.Monthly, RecurrenceFrequency.Quarterly, RecurrenceFrequency.Yearly];

    public IReadOnlyList<AutoGenerationMode> ModeOptions { get; } =
        [AutoGenerationMode.PendingConfirm, AutoGenerationMode.AutoApply];

    public IReadOnlyList<TradeType> TradeTypeOptions { get; } =
        [TradeType.Withdrawal, TradeType.Income, TradeType.Deposit];

    public RecurringViewModel(
        IRecurringTransactionRepository recurringRepo,
        IPendingRecurringEntryRepository pendingRepo,
        RecurringTransactionScheduler scheduler,
        ISnackbarService snackbar,
        ILocalizationService localization)
    {
        _recurringRepo = recurringRepo;
        _pendingRepo = pendingRepo;
        _scheduler = scheduler;
        _snackbar = snackbar;
        _localization = localization;

        Subscriptions = new ReadOnlyObservableCollection<RecurringRowViewModel>(_subscriptions);
        Pending = new ReadOnlyObservableCollection<PendingRecurringRowViewModel>(_pending);

        _pending.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(HasPending));
            OnPropertyChanged(nameof(HasNoPending));
            OnPropertyChanged(nameof(PendingBadge));
        };

        _subscriptions.CollectionChanged += (_, _) =>
        {
            RefreshSubscriptionSummary();
        };

        _localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var row in Subscriptions)
            row.RefreshLocalizedDisplay();
    }

    public async Task LoadAsync()
    {
        await LoadSubscriptionsAsync().ConfigureAwait(true);
        await LoadPendingAsync().ConfigureAwait(true);
        IsLoaded = true;
    }

    private async Task LoadSubscriptionsAsync()
    {
        var data = await _recurringRepo.GetAllAsync().ConfigureAwait(true);
        _subscriptions.Clear();
        foreach (var r in data)
            _subscriptions.Add(RecurringRowViewModel.FromModel(r));
    }

    private async Task LoadPendingAsync()
    {
        var data = await _pendingRepo.GetByStatusAsync(PendingStatus.Pending).ConfigureAwait(true);
        _pending.Clear();
        foreach (var e in data)
        {
            var sourceName = _subscriptions.FirstOrDefault(s => s.Id == e.RecurringSourceId)?.Name ?? "—";
            _pending.Add(PendingRecurringRowViewModel.FromModel(e, sourceName));
        }
    }

    [RelayCommand]
    private void OpenAddForm()
    {
        AddError = string.Empty;
        AddName = string.Empty;
        AddAmount = 0m;
        AddInterval = 1;
        AddStartDate = DateTime.Today;
        AddNote = string.Empty;
        IsAddFormOpen = true;
    }

    [RelayCommand]
    private void CloseAddForm()
    {
        IsAddFormOpen = false;
        AddError = string.Empty;
    }

    [RelayCommand]
    private async Task AddSubscriptionAsync()
    {
        AddError = string.Empty;
        var name = AddName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            AddError = GetString("Recurring.Error.NameRequired", "請輸入名稱");
            return;
        }
        if (AddAmount <= 0m)
        {
            AddError = GetString("Recurring.Error.AmountRequired", "請輸入大於 0 的金額");
            return;
        }
        if (AddInterval < 1)
        {
            AddError = GetString("Recurring.Error.IntervalInvalid", "間隔必須 ≥ 1");
            return;
        }
        if (!TradeTypeOptions.Contains(AddTradeType))
        {
            AddError = GetString("Recurring.Error.UnsupportedTradeType", "此交易類型目前不支援訂閱排程");
            return;
        }

        var recurring = new RecurringTransaction(
            Id: Guid.NewGuid(),
            Name: name,
            TradeType: AddTradeType,
            Amount: AddAmount,
            CashAccountId: null,
            CategoryId: null,
            Frequency: AddFrequency,
            Interval: AddInterval,
            StartDate: AddStartDate,
            EndDate: null,
            GenerationMode: AddGenerationMode,
            LastGeneratedAt: null,
            NextDueAt: AddStartDate,
            Note: NullIfBlank(AddNote),
            IsEnabled: true);

        await _recurringRepo.AddAsync(recurring).ConfigureAwait(true);
        _subscriptions.Add(RecurringRowViewModel.FromModel(recurring));

        AddName = string.Empty;
        AddAmount = 0m;
        AddNote = string.Empty;
        IsAddFormOpen = false;

        _snackbar.Success(string.Format(
            GetString("Recurring.Toast.Added", "已新增訂閱「{0}」"), name));
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(RecurringRowViewModel row)
    {
        if (row is null)
            return;
        row.IsEnabled = !row.IsEnabled;
        await _recurringRepo.UpdateAsync(row.ToModel()).ConfigureAwait(true);
        RefreshSubscriptionSummary();
    }

    [RelayCommand]
    private async Task DeleteSubscriptionAsync(RecurringRowViewModel row)
    {
        if (row is null)
            return;
        await _pendingRepo.RemoveByRecurringSourceAsync(row.Id).ConfigureAwait(true);
        await _recurringRepo.RemoveAsync(row.Id).ConfigureAwait(true);
        foreach (var pending in _pending.Where(x => x.RecurringSourceId == row.Id).ToList())
            _pending.Remove(pending);
        _subscriptions.Remove(row);
        _snackbar.Success(string.Format(
            GetString("Recurring.Toast.Deleted", "已刪除「{0}」"), row.Name));
    }

    [RelayCommand]
    private void RequestDeleteSubscription(RecurringRowViewModel row)
    {
        if (row is null)
            return;
        _pendingDelete = row;
        DeleteTargetName = row.Name;
        IsDeleteConfirmOpen = true;
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        var row = _pendingDelete;
        CancelDelete();
        if (row is not null)
            await DeleteSubscriptionAsync(row).ConfigureAwait(true);
    }

    [RelayCommand]
    private void CancelDelete()
    {
        _pendingDelete = null;
        DeleteTargetName = string.Empty;
        IsDeleteConfirmOpen = false;
    }

    private bool CanRunScheduler() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRunScheduler))]
    private async Task RunSchedulerAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try
        {
            var result = await _scheduler.RunAsync(DateTime.Now).ConfigureAwait(true);
            RunStatus = string.Format(
                GetString("Recurring.Run.Status", "已套用 {0} 筆，新增 {1} 筆待確認"),
                result.AutoApplied, result.PendingCreated);
            await LoadAsync().ConfigureAwait(true);
            _snackbar.Success(RunStatus);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ConfirmPendingAsync(PendingRecurringRowViewModel row)
    {
        if (row is null)
            return;
        await _scheduler.ConfirmAsync(row.Id).ConfigureAwait(true);
        _pending.Remove(row);
        _snackbar.Success(GetString("Recurring.Pending.Toast.Confirmed", "已確認"));
    }

    [RelayCommand]
    private async Task SkipPendingAsync(PendingRecurringRowViewModel row)
    {
        if (row is null)
            return;
        await _scheduler.SkipAsync(row.Id).ConfigureAwait(true);
        _pending.Remove(row);
        _snackbar.Success(GetString("Recurring.Pending.Toast.Skipped", "已略過"));
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private string GetString(string key, string fallback) =>
        _localization.Get(key, fallback);

    private void RefreshSubscriptionSummary()
    {
        OnPropertyChanged(nameof(SubscriptionCount));
        OnPropertyChanged(nameof(EnabledSubscriptionCount));
        OnPropertyChanged(nameof(DisabledSubscriptionCount));
        OnPropertyChanged(nameof(HasNoSubscriptions));
        OnPropertyChanged(nameof(HasSubscriptions));
    }
}
