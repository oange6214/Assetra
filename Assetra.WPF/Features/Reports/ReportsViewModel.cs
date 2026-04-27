using Assetra.Application.Reports.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Interfaces.Reports;
using Assetra.Core.Models;
using Assetra.Core.Models.Analysis;
using Assetra.Core.Models.Reports;
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
    private readonly IIncomeStatementService? _incomeService;
    private readonly IBalanceSheetService? _balanceService;
    private readonly ICashFlowStatementService? _cashFlowService;
    private readonly IReportExportService? _exportService;
    private readonly IMoneyWeightedReturnCalculator? _mwrCalculator;
    private readonly IPnlAttributionService? _attribution;
    private readonly IBenchmarkComparisonService? _benchmark;

    [ObservableProperty]
    private PerformanceResult? _performance;

    [ObservableProperty]
    private IncomeStatement? _incomeStatement;

    [ObservableProperty]
    private BalanceSheet? _balanceSheet;

    [ObservableProperty]
    private CashFlowStatement? _cashFlowStatement;

    [ObservableProperty]
    private string? _exportStatus;

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
        ILocalizationService? localization = null,
        IIncomeStatementService? incomeService = null,
        IBalanceSheetService? balanceService = null,
        ICashFlowStatementService? cashFlowService = null,
        IReportExportService? exportService = null,
        IMoneyWeightedReturnCalculator? mwrCalculator = null,
        IPnlAttributionService? attribution = null,
        IBenchmarkComparisonService? benchmark = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _currency = currency;
        _localization = localization;
        _incomeService = incomeService;
        _balanceService = balanceService;
        _cashFlowService = cashFlowService;
        _exportService = exportService;
        _mwrCalculator = mwrCalculator;
        _attribution = attribution;
        _benchmark = benchmark;

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

    public IReadOnlyList<OverBudgetRowViewModel> OverBudgetRows =>
        OverBudgetCategories
            .Select(row => new OverBudgetRowViewModel(
                row,
                FormatAmount(row.Spent),
                row.BudgetAmount.HasValue ? $"/ {FormatAmount(row.BudgetAmount.Value)}" : string.Empty))
            .ToArray();

    public IReadOnlyList<UpcomingRowViewModel> UpcomingRows =>
        Upcoming
            .Select(row => new UpcomingRowViewModel(
                row.Name,
                row.DueDate,
                FormatAmount(row.Amount)))
            .ToArray();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            Report = await _service.BuildAsync(Year, Month).ConfigureAwait(true);
            await LoadStatementsAsync().ConfigureAwait(true);
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

    private async Task LoadStatementsAsync()
    {
        var period = ReportPeriod.Month(Year, Month);
        if (_incomeService is not null)
            IncomeStatement = await _incomeService.GenerateAsync(period).ConfigureAwait(true);
        if (_balanceService is not null)
            BalanceSheet = await _balanceService.GenerateAsync(period.End).ConfigureAwait(true);
        if (_cashFlowService is not null)
            CashFlowStatement = await _cashFlowService.GenerateAsync(period).ConfigureAwait(true);
        await LoadPerformanceAsync().ConfigureAwait(true);
    }

    private async Task LoadPerformanceAsync()
    {
        if (_mwrCalculator is null && _attribution is null) return;
        var perfPeriod = PerformancePeriod.Month(Year, Month);
        var mwr = _mwrCalculator is null ? null : await _mwrCalculator.ComputeAsync(perfPeriod).ConfigureAwait(true);
        var buckets = _attribution is null
            ? (IReadOnlyList<AttributionBucket>)Array.Empty<AttributionBucket>()
            : await _attribution.ComputeAsync(perfPeriod).ConfigureAwait(true);
        decimal? bench = null;
        if (_benchmark is not null)
        {
            try { bench = await _benchmark.ComputeBenchmarkTwrAsync("0050.TW", perfPeriod).ConfigureAwait(true); }
            catch { bench = null; }
        }
        Performance = new PerformanceResult(perfPeriod, Xirr: mwr, Twr: null, Mwr: mwr, BenchmarkTwr: bench, Attribution: buckets);
    }

    [RelayCommand]
    private Task ExportIncomePdfAsync() => ExportAsync("IncomeStatement", "pdf");

    [RelayCommand]
    private Task ExportIncomeCsvAsync() => ExportAsync("IncomeStatement", "csv");

    [RelayCommand]
    private Task ExportBalancePdfAsync() => ExportAsync("BalanceSheet", "pdf");

    [RelayCommand]
    private Task ExportBalanceCsvAsync() => ExportAsync("BalanceSheet", "csv");

    [RelayCommand]
    private Task ExportCashFlowPdfAsync() => ExportAsync("CashFlow", "pdf");

    [RelayCommand]
    private Task ExportCashFlowCsvAsync() => ExportAsync("CashFlow", "csv");

    private async Task ExportAsync(string target, string formatStr)
    {
        if (_exportService is null) { ExportStatus = "Export service not available."; return; }
        var format = formatStr == "pdf" ? ExportFormat.Pdf : ExportFormat.Csv;
        var ext = format == ExportFormat.Pdf ? "pdf" : "csv";
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"{target}_{Year:D4}-{Month:D2}.{ext}",
            DefaultExt = ext,
            Filter = format == ExportFormat.Pdf ? "PDF (*.pdf)|*.pdf" : "CSV (*.csv)|*.csv",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            switch (target)
            {
                case "IncomeStatement" when IncomeStatement is not null:
                    await _exportService.ExportAsync(IncomeStatement, format, dlg.FileName).ConfigureAwait(true);
                    break;
                case "BalanceSheet" when BalanceSheet is not null:
                    await _exportService.ExportAsync(BalanceSheet, format, dlg.FileName).ConfigureAwait(true);
                    break;
                case "CashFlow" when CashFlowStatement is not null:
                    await _exportService.ExportAsync(CashFlowStatement, format, dlg.FileName).ConfigureAwait(true);
                    break;
                default:
                    ExportStatus = "No data to export.";
                    return;
            }
            ExportStatus = $"Exported: {dlg.FileName}";
        }
        catch (Exception ex)
        {
            ExportStatus = $"Export failed: {ex.Message}";
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
        OnPropertyChanged(nameof(OverBudgetRows));
        OnPropertyChanged(nameof(UpcomingRows));
    }

    public sealed record OverBudgetRowViewModel(
        CategorySpendSummary Summary,
        string SpentDisplay,
        string BudgetDisplay)
    {
        public string CategoryName => Summary.CategoryName;
        public decimal? OveragePercent => Summary.OveragePercent;
    }

    public sealed record UpcomingRowViewModel(
        string Name,
        DateTime DueDate,
        string AmountDisplay);
}
