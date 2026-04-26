using Assetra.Application.Reports.Services;
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

    public ReportsViewModel(MonthEndReportService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;

        var today = DateTime.Today;
        _year = today.Year;
        _month = today.Month;
    }

    public string MonthHeader => $"{Year}-{Month:D2}";
    public bool   HasReport   => Report is not null;

    public string IncomeDisplay  => Report is null ? "—" : $"NT${Report.Current.TotalIncome:N0}";
    public string ExpenseDisplay => Report is null ? "—" : $"NT${Report.Current.TotalExpense:N0}";
    public string NetDisplay     => Report is null ? "—" : $"NT${Report.Current.NetCashFlow:N0}";

    public string IncomeDeltaDisplay  => FormatDelta(Report?.IncomeDelta);
    public string ExpenseDeltaDisplay => FormatDelta(Report?.ExpenseDelta);
    public string NetDeltaDisplay     => FormatDelta(Report?.NetDelta);

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

    private static string FormatDelta(decimal? value)
    {
        if (value is not { } v) return "—";
        var sign = v >= 0 ? "+" : "-";
        return $"{sign}NT${Math.Abs(v):N0}";
    }
}
