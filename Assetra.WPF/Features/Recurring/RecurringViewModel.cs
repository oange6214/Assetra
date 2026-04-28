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

    public ObservableCollection<RecurringRowViewModel> Subscriptions { get; } = [];
    public ObservableCollection<PendingRecurringRowViewModel> Pending { get; } = [];

    public int PendingCount => Pending.Count;
    public bool HasPending  => Pending.Count > 0;
    public string PendingBadge => Pending.Count > 99 ? "99+" : Pending.Count.ToString();

    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _runStatus = string.Empty;

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

        Pending.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(HasPending));
            OnPropertyChanged(nameof(PendingBadge));
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
        Subscriptions.Clear();
        foreach (var r in data)
            Subscriptions.Add(RecurringRowViewModel.FromModel(r));
    }

    private async Task LoadPendingAsync()
    {
        var data = await _pendingRepo.GetByStatusAsync(PendingStatus.Pending).ConfigureAwait(true);
        Pending.Clear();
        foreach (var e in data)
        {
            var sourceName = Subscriptions.FirstOrDefault(s => s.Id == e.RecurringSourceId)?.Name ?? "—";
            Pending.Add(PendingRecurringRowViewModel.FromModel(e, sourceName));
        }
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
        Subscriptions.Add(RecurringRowViewModel.FromModel(recurring));

        AddName = string.Empty;
        AddAmount = 0m;
        AddNote = string.Empty;

        _snackbar.Success(string.Format(
            GetString("Recurring.Toast.Added", "已新增訂閱「{0}」"), name));
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(RecurringRowViewModel row)
    {
        if (row is null) return;
        row.IsEnabled = !row.IsEnabled;
        await _recurringRepo.UpdateAsync(row.ToModel()).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteSubscriptionAsync(RecurringRowViewModel row)
    {
        if (row is null) return;
        await _pendingRepo.RemoveByRecurringSourceAsync(row.Id).ConfigureAwait(true);
        await _recurringRepo.RemoveAsync(row.Id).ConfigureAwait(true);
        foreach (var pending in Pending.Where(x => x.RecurringSourceId == row.Id).ToList())
            Pending.Remove(pending);
        Subscriptions.Remove(row);
        _snackbar.Success(string.Format(
            GetString("Recurring.Toast.Deleted", "已刪除「{0}」"), row.Name));
    }

    [RelayCommand]
    private async Task RunSchedulerAsync()
    {
        if (IsBusy) return;
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
        if (row is null) return;
        await _scheduler.ConfirmAsync(row.Id).ConfigureAwait(true);
        Pending.Remove(row);
        _snackbar.Success(GetString("Recurring.Pending.Toast.Confirmed", "已確認"));
    }

    [RelayCommand]
    private async Task SkipPendingAsync(PendingRecurringRowViewModel row)
    {
        if (row is null) return;
        await _scheduler.SkipAsync(row.Id).ConfigureAwait(true);
        Pending.Remove(row);
        _snackbar.Success(GetString("Recurring.Pending.Toast.Skipped", "已略過"));
    }

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private string GetString(string key, string fallback) =>
        _localization.Get(key, fallback);
}
