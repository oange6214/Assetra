using Assetra.Application.Reports.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Reports;

/// <summary>
/// 月結報告檢視模型：以 (Year, Month) 為核心狀態，呼叫 <see cref="MonthEndReportService"/>
/// 取得當月 vs 上月對照、預算超支與未來 14 天到期訂閱清單。
/// </summary>
public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly MonthEndReportService _service;
    private readonly ICurrencyService? _currency;
    private readonly ILocalizationService? _localization;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthHeader))]
    private int _year;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MonthHeader))]
    private int _month;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport))]
    [NotifyPropertyChangedFor(nameof(IncomeDeltaDisplay))]
    [NotifyPropertyChangedFor(nameof(ExpenseDeltaDisplay))]
    [NotifyPropertyChangedFor(nameof(NetDeltaDisplay))]
    [NotifyPropertyChangedFor(nameof(SavingsRateDisplay))]
    [NotifyPropertyChangedFor(nameof(IncomeDisplay))]
    [NotifyPropertyChangedFor(nameof(ExpenseDisplay))]
    [NotifyPropertyChangedFor(nameof(NetDisplay))]
    [NotifyPropertyChangedFor(nameof(IsIncomeUp))]
    [NotifyPropertyChangedFor(nameof(IsExpenseUp))]
    [NotifyPropertyChangedFor(nameof(IsNetUp))]
    [NotifyPropertyChangedFor(nameof(HasOverBudget))]
    [NotifyPropertyChangedFor(nameof(HasUpcoming))]
    [NotifyPropertyChangedFor(nameof(OverBudgetCategories))]
    [NotifyPropertyChangedFor(nameof(Upcoming))]
    private MonthEndReport? _report;

    public ReportsViewModel(
        MonthEndReportService service,
        ICurrencyService? currency = null,
        ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _currency = currency;
        _localization = localization;

        var today = DateTime.Today;
        _year = today.Year;
        _month = today.Month;

        if (_currency is not null)
            _currency.CurrencyChanged += OnCurrencyChanged;
        if (_localization is not null)
            _localization.LanguageChanged += OnLanguageChanged;
    }

    public string MonthHeader => $"{Year}-{Month:D2}";
    public bool   HasReport   => Report is not null;

    public string IncomeDisplay  => Report is null ? "—" : FormatAmount(Report.Current.TotalIncome);
    public string ExpenseDisplay => Report is null ? "—" : FormatAmount(Report.Current.TotalExpense);
    public string NetDisplay     => Report is null ? "—" : FormatAmount(Report.Current.NetCashFlow);

    public string IncomeDeltaDisplay  => FormatDeltaInstance(Report?.IncomeDelta);
    public string ExpenseDeltaDisplay => FormatDeltaInstance(Report?.ExpenseDelta);
    public string NetDeltaDisplay     => FormatDeltaInstance(Report?.NetDelta);

    public bool IsIncomeUp  => (Report?.IncomeDelta  ?? 0m) >= 0m;
    public bool IsExpenseUp => (Report?.ExpenseDelta ?? 0m) >= 0m;
    public bool IsNetUp     => (Report?.NetDelta     ?? 0m) >= 0m;

    public string SavingsRateDisplay =>
        Report is null ? "—" : $"{Report.SavingsRate * 100m:F1}%";

    public bool HasOverBudget => Report is { OverBudgetCategories.Count: > 0 };
    public bool HasUpcoming   => Report is { Upcoming.Count: > 0 };

    public IReadOnlyList<CategorySpendSummary> OverBudgetCategories =>
        Report?.OverBudgetCategories ?? [];

    public IReadOnlyList<UpcomingRecurringItem> Upcoming =>
        Report?.Upcoming ?? [];

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            Report = await _service.BuildAsync(Year, Month).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Report = null;
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task PrevMonthAsync()
    {
        if (Month == 1) { Year--; Month = 12; }
        else            { Month--; }
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task NextMonthAsync()
    {
        if (Month == 12) { Year++; Month = 1; }
        else             { Month++; }
        await LoadAsync().ConfigureAwait(true);
    }

    private string FormatDeltaInstance(decimal? value)
    {
        if (value is not { } v) return "—";
        return _currency?.FormatSigned(v)
               ?? (v >= 0 ? $"+NT${Math.Abs(v):N0}" : $"-NT${Math.Abs(v):N0}");
    }

    private string FormatAmount(decimal value) =>
        _currency?.FormatAmount(value) ?? $"NT${value:N0}";

    private void OnCurrencyChanged() => RaiseDisplayStrings();

    private void OnLanguageChanged(object? sender, EventArgs e) => RaiseDisplayStrings();

    private void RaiseDisplayStrings()
    {
        OnPropertyChanged(nameof(IncomeDisplay));
        OnPropertyChanged(nameof(ExpenseDisplay));
        OnPropertyChanged(nameof(NetDisplay));
        OnPropertyChanged(nameof(IncomeDeltaDisplay));
        OnPropertyChanged(nameof(ExpenseDeltaDisplay));
        OnPropertyChanged(nameof(NetDeltaDisplay));
    }
}
