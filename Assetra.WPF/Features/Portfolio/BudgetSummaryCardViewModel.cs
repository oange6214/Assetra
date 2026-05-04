using System.Collections.ObjectModel;
using Assetra.Application.Budget.Services;
using Assetra.Core.Models;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Assetra.WPF.Features.Portfolio;

public sealed partial class BudgetSummaryCardViewModel : ObservableObject
{
    private readonly MonthlyBudgetSummaryService _service;
    private readonly IBudgetRefreshNotifier _budgetRefreshNotifier;

    [ObservableProperty] private int _year = DateTime.Today.Year;
    [ObservableProperty] private int _month = DateTime.Today.Month;

    [ObservableProperty] private decimal _totalIncome;
    [ObservableProperty] private decimal _totalExpense;
    [ObservableProperty] private decimal _netCashFlow;
    [ObservableProperty] private decimal? _totalBudget;
    [ObservableProperty] private double _budgetUsageRatio;
    [ObservableProperty] private bool _hasData;

    public ObservableCollection<CategorySpendSummary> TopCategories { get; } = [];

    public string PeriodDisplay => $"{Year}-{Month:D2}";
    public string TotalIncomeDisplay => $"NT${TotalIncome:N0}";
    public string TotalExpenseDisplay => $"NT${TotalExpense:N0}";
    public string NetCashFlowDisplay =>
        (NetCashFlow >= 0 ? "+" : "-") + $"NT${Math.Abs(NetCashFlow):N0}";
    public string TotalBudgetDisplay =>
        TotalBudget is { } b ? $"NT${b:N0}" : "—";
    public bool IsNetPositive => NetCashFlow >= 0;
    public bool IsOverBudget => TotalBudget is { } b && TotalExpense > b;

    partial void OnYearChanged(int value) => OnPropertyChanged(nameof(PeriodDisplay));
    partial void OnMonthChanged(int value) => OnPropertyChanged(nameof(PeriodDisplay));
    partial void OnTotalIncomeChanged(decimal value) => OnPropertyChanged(nameof(TotalIncomeDisplay));
    partial void OnTotalExpenseChanged(decimal value)
    {
        OnPropertyChanged(nameof(TotalExpenseDisplay));
        OnPropertyChanged(nameof(IsOverBudget));
    }
    partial void OnNetCashFlowChanged(decimal value)
    {
        OnPropertyChanged(nameof(NetCashFlowDisplay));
        OnPropertyChanged(nameof(IsNetPositive));
    }
    partial void OnTotalBudgetChanged(decimal? value)
    {
        OnPropertyChanged(nameof(TotalBudgetDisplay));
        OnPropertyChanged(nameof(IsOverBudget));
    }

    public BudgetSummaryCardViewModel(
        MonthlyBudgetSummaryService service,
        IBudgetRefreshNotifier budgetRefreshNotifier)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(budgetRefreshNotifier);
        _service = service;
        _budgetRefreshNotifier = budgetRefreshNotifier;
        _budgetRefreshNotifier.BudgetChanged += OnBudgetChanged;
    }

    private void OnBudgetChanged(object? sender, EventArgs e) => AsyncHelpers.SafeFireAndForget(LoadAsync, "BudgetSummary.LoadOnBudgetChange");

    [RelayCommand]
    public async Task LoadAsync()
    {
        var summary = await _service.BuildAsync(Year, Month).ConfigureAwait(true);
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            TotalIncome = summary.TotalIncome;
            TotalExpense = summary.TotalExpense;
            NetCashFlow = summary.NetCashFlow;
            TotalBudget = summary.TotalBudget;
            BudgetUsageRatio = summary.TotalBudget is { } b && b > 0
                ? Math.Min(1.0, (double)(summary.TotalExpense / b))
                : 0;

            TopCategories.Clear();
            foreach (var c in summary.Categories.Take(5))
                TopCategories.Add(c);
            HasData = summary.TotalIncome != 0 || summary.TotalExpense != 0;
        });
    }
}
