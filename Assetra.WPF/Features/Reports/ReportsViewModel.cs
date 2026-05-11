using System.Collections.ObjectModel;
using System.Net.Http;
using Assetra.Application.Fx;
using Assetra.Application.Reports.Services;
using Assetra.Application.Tax;
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
    private readonly ITradeRepository? _trades;
    private readonly IPortfolioRepository? _portfolio;
    private readonly IMultiCurrencyValuationService? _fx;
    // Performance / Risk 計算服務（XirrCalculator, TwrCalculator, MwrCalculator,
    // PnlAttributionService, BenchmarkComparisonService, VolatilityCalculator,
    // DrawdownCalculator, SharpeRatioCalculator, ConcentrationAnalyzer,
    // PortfolioSnapshotRepository）已隨 Performance/Risk Expander 移除一併
    // 從 Reports 拔除 — 這些指標已搬到「財務概覽 → 資產趨勢」tab。
    // 服務本身在 DI 仍註冊，由 PortfolioHistoryViewModel 取用。
    private readonly IAppSettingsService? _appSettings;
    private readonly ITaxProfileProvider? _taxProfiles;
    private readonly AnnualTaxComputationService? _annualTaxComputer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIncomeStatement))]
    private IncomeStatement? _incomeStatement;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBalanceSheet))]
    private BalanceSheet? _balanceSheet;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCashFlowStatement))]
    private CashFlowStatement? _cashFlowStatement;

    /// <summary>
    /// Annual tax summary for the current <see cref="Year"/>. Computed in-process
    /// by <see cref="TaxCalculationService.CalculateForYear"/> over the trade
    /// repository — no separate service interface needed since it's a pure
    /// function. Refreshed on Year change (Month change does NOT trigger
    /// recompute since tax is a yearly aggregate).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTaxSummary))]
    private TaxSummary? _taxSummary;

    /// <summary>
    /// Per-year tax aggregates for the multi-year comparison panel. Computed by
    /// scanning the trade journal once and grouping by year. Years with NO
    /// dividend or capital-gain records are still included (zero rows) so the
    /// table doesn't have gaps.
    /// </summary>
    private readonly ObservableCollection<TaxYearRowViewModel> _multiYearTaxRows = new();
    public ReadOnlyObservableCollection<TaxYearRowViewModel> MultiYearTaxRows { get; }

    /// <summary>True when MultiYearTaxRows has ≥ 2 rows — single-year users get
    /// the regular TaxSummary card instead of an unnecessary one-row table.</summary>
    public bool HasMultiYearTax => _multiYearTaxRows.Count >= 2;

    /// <summary>
    /// AMT computation result for the current <see cref="TaxSummary"/> using
    /// the parameters from <see cref="AppSettings"/>. Null until trades load.
    /// Even when not "applicable" (海外所得未達門檻 or 使用者未填一般所得),
    /// the result still flows through so the UI can show the formula breakdown
    /// for the user's awareness.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAmtResult))]
    private AmtCalculationResult? _amtResult;

    public bool HasAmtResult => AmtResult is not null && TaxSummary is not null;

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
    [NotifyPropertyChangedFor(nameof(OverBudgetRows))]
    [NotifyPropertyChangedFor(nameof(UpcomingRows))]
    private MonthEndReport? _report;

    public ReportsViewModel(
        MonthEndReportService service,
        ICurrencyService? currency = null,
        ILocalizationService? localization = null,
        IIncomeStatementService? incomeService = null,
        IBalanceSheetService? balanceService = null,
        ICashFlowStatementService? cashFlowService = null,
        IReportExportService? exportService = null,
        ITradeRepository? trades = null,
        IPortfolioRepository? portfolio = null,
        IMultiCurrencyValuationService? fx = null,
        IAppSettingsService? appSettings = null,
        ITaxProfileProvider? taxProfiles = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _currency = currency;
        _localization = localization;
        _incomeService = incomeService;
        _balanceService = balanceService;
        _cashFlowService = cashFlowService;
        _exportService = exportService;
        _trades = trades;
        _portfolio = portfolio;
        _fx = fx;
        _appSettings = appSettings;
        _taxProfiles = taxProfiles ?? new EmbeddedTaxProfileProvider();
        _annualTaxComputer = new AnnualTaxComputationService(_taxProfiles);
        MultiYearTaxRows = new ReadOnlyObservableCollection<TaxYearRowViewModel>(_multiYearTaxRows);

        var today = DateTime.Today;
        _year = today.Year;
        _month = today.Month;

        // Year picker covers 10 years back to 1 year forward — wide enough
        // for historical review yet tight enough to keep the dropdown short.
        YearOptions = Enumerable.Range(today.Year - 10, 12).ToList();
        MonthOptions = Enumerable.Range(1, 12).ToList();

        if (_currency is not null)
            _currency.CurrencyChanged += OnCurrencyChanged;
        if (_localization is not null)
            _localization.LanguageChanged += OnLanguageChanged;
        if (_appSettings is not null)
            _appSettings.Changed += OnSettingsChanged;
    }

    public IReadOnlyList<int> YearOptions { get; }
    public IReadOnlyList<int> MonthOptions { get; }

    /// <summary>
    /// Suppresses auto-reload while the prev/next month commands mutate
    /// both Year and Month — without this, going from Jan to Dec of the
    /// previous year would trigger two reloads.
    /// </summary>
    private bool _suppressAutoLoad;

    partial void OnYearChanged(int value)
    {
        if (_suppressAutoLoad) return;
        _ = LoadAsync();
    }

    partial void OnMonthChanged(int value)
    {
        if (_suppressAutoLoad) return;
        _ = LoadAsync();
    }

    public string MonthHeader => $"{Year}-{Month:D2}";
    public bool   HasReport   => Report is not null;
    public bool HasIncomeStatement => IncomeStatement is not null;

    public bool HasTaxSummary => TaxSummary is not null
        && (TaxSummary.Dividends.Count > 0 || TaxSummary.CapitalGains.Count > 0);
    public bool HasBalanceSheet => BalanceSheet is not null;
    public bool HasCashFlowStatement => CashFlowStatement is not null;

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

    // PerformanceAttributionRows / RiskTopHoldingRows getters 移除 —
    // 對應 Performance / Risk Expander 已從 ReportsView 拔除。

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = null;
        ClearReportDetails();
        try
        {
            try
            {
                Report = await _service.BuildAsync(Year, Month).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                Report = null;
                ClearReportDetails();
                return;
            }

            try
            {
                await LoadStatementsAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                Report = null;
                ClearReportDetails();
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadStatementsAsync()
    {
        var period = ReportPeriod.Month(Year, Month);
        var detailErrors = new List<string>();

        if (_incomeService is not null)
        {
            try
            {
                IncomeStatement = await _incomeService.GenerateAsync(period).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                IncomeStatement = null;
                detailErrors.Add(ex.Message);
            }
        }

        if (_balanceService is not null)
        {
            try
            {
                BalanceSheet = await _balanceService.GenerateAsync(period.End).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                BalanceSheet = null;
                detailErrors.Add(ex.Message);
            }
        }

        if (_cashFlowService is not null)
        {
            try
            {
                CashFlowStatement = await _cashFlowService.GenerateAsync(period).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                CashFlowStatement = null;
                detailErrors.Add(ex.Message);
            }
        }

        if (_trades is not null)
        {
            try
            {
                // Tax is annual — pass the year from the dialog state. Re-runs
                // every Month change too (cheap) so the user sees tax data
                // even from the income/balance tabs without switching tabs.
                var allTrades = (await _trades.GetAllAsync().ConfigureAwait(true)).ToList();

                // 建構年度報稅 inputs（filing 與 amtInputs 由 AppSettings 投影）
                var (filing, amtInputs) = BuildTaxInputs();

                // 焦點年度完整試算
                var focalProfile = _taxProfiles!.Get(Year);
                var focalSummary = Assetra.Application.Tax.TaxCalculationService.CalculateForYear(
                    Year, allTrades, focalProfile);
                TaxSummary = focalSummary;

                // Multi-year comparison: 每個有交易的年度跑完整 AnnualTaxComputation
                var taxYears = allTrades
                    .Where(t => t.Type == Assetra.Core.Models.TradeType.CashDividend
                             || (t.Type == Assetra.Core.Models.TradeType.Sell && t.RealizedPnl is not null))
                    .Select(t => t.TradeDate.Year)
                    .Distinct()
                    .OrderByDescending(y => y)
                    .ToList();

                _multiYearTaxRows.Clear();
                AnnualTaxComputation? focalComputation = null;
                foreach (var y in taxYears)
                {
                    var c = _annualTaxComputer!.Compute(y, allTrades, filing, amtInputs);
                    _multiYearTaxRows.Add(TaxYearRowViewModel.FromComputation(c));
                    if (y == Year) focalComputation = c;
                }
                OnPropertyChanged(nameof(HasMultiYearTax));

                // AMT result 取焦點年度（若該年無交易，仍用 AnnualTaxComputer 跑一次）
                focalComputation ??= _annualTaxComputer!.Compute(Year, allTrades, filing, amtInputs);
                AmtResult = focalComputation.Amt;
            }
            catch (Exception ex)
            {
                TaxSummary = null;
                _multiYearTaxRows.Clear();
                OnPropertyChanged(nameof(HasMultiYearTax));
                AmtResult = null;
                detailErrors.Add(ex.Message);
            }
        }

        // Performance / Risk 計算已搬到「財務概覽 → 資產趨勢」tab。Reports
        // 不再執行這些計算，連帶省 50–200ms 載入時間。

        if (detailErrors.Count > 0)
            throw new InvalidOperationException(string.Join(" / ", detailErrors.Distinct()));
    }

    private void ClearReportDetails()
    {
        IncomeStatement = null;
        BalanceSheet = null;
        CashFlowStatement = null;
        TaxSummary = null;
        _multiYearTaxRows.Clear();
        OnPropertyChanged(nameof(HasMultiYearTax));
        AmtResult = null;
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

    [RelayCommand]
    private Task ExportTaxPdfAsync() => ExportAsync("TaxSummary", "pdf");

    [RelayCommand]
    private Task ExportTaxCsvAsync() => ExportAsync("TaxSummary", "csv");

    private async Task ExportAsync(string target, string formatStr)
    {
        if (_exportService is null)
        {
            ExportStatus = GetString("Reports.Export.Status.Unavailable", "匯出服務目前不可用。");
            return;
        }
        var format = formatStr == "pdf" ? ExportFormat.Pdf : ExportFormat.Csv;
        var ext = format == ExportFormat.Pdf ? "pdf" : "csv";
        // Tax is an annual aggregate — drop the month suffix from its file name
        // so successive year-end exports don't accidentally collide on Month.
        var fileBase = target == "TaxSummary"
            ? $"{target}_{Year:D4}"
            : $"{target}_{Year:D4}-{Month:D2}";
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"{fileBase}.{ext}",
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
                case "TaxSummary" when TaxSummary is not null:
                    await _exportService.ExportAsync(TaxSummary, format, dlg.FileName).ConfigureAwait(true);
                    break;
                default:
                    ExportStatus = GetString("Reports.Export.Status.Empty", "目前沒有可匯出的資料。");
                    return;
            }
            ExportStatus = string.Format(
                GetString("Reports.Export.Status.Success", "已匯出：{0}"),
                dlg.FileName);
        }
        catch (Exception ex)
        {
            ExportStatus = string.Format(
                GetString("Reports.Export.Status.Failed", "匯出失敗：{0}"),
                ex.Message);
        }
    }

    [RelayCommand]
    private async Task PrevMonthAsync()
    {
        _suppressAutoLoad = true;
        try
        {
            if (Month == 1) { Year--; Month = 12; }
            else            { Month--; }
        }
        finally { _suppressAutoLoad = false; }
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task NextMonthAsync()
    {
        _suppressAutoLoad = true;
        try
        {
            if (Month == 12) { Year++; Month = 1; }
            else             { Month++; }
        }
        finally { _suppressAutoLoad = false; }
        await LoadAsync().ConfigureAwait(true);
    }

    private string FormatDeltaInstance(decimal? value)
    {
        if (value is not { } v) return "—";
        return _currency?.FormatSigned(v)
               ?? (v >= 0 ? $"+NT${Math.Abs(v):N0}" : $"-NT${Math.Abs(v):N0}");
    }

    /// <summary>
    /// 從 AppSettings 投影出 (TaxFilingProfile, AmtCalculationParameters) — 兩者都是
    /// AnnualTaxComputationService 的入口參數。AppSettings 為 null（測試 / 早期啟動）
    /// 時回傳全 0 預設值，計算服務仍會用 trade-derived 股利當作 fallback。
    /// </summary>
    private (TaxFilingProfile filing, AmtCalculationParameters amtInputs) BuildTaxInputs()
    {
        var s = _appSettings?.Current;
        var filing = new TaxFilingProfile(
            IsMarried: s?.TaxIsMarried ?? false,
            DependentCount: s?.TaxDependentCount ?? 0,
            PreschoolCount: s?.TaxPreschoolCount ?? 0,
            CollegeStudentCount: s?.TaxCollegeStudentCount ?? 0,
            LongCareCount: s?.TaxLongCareCount ?? 0,
            DisabilityCount: s?.TaxDisabilityCount ?? 0,
            SalaryIncome: s?.TaxSalaryIncome ?? 0m,
            DividendIncome: 0m,   // 由 AnnualTaxComputationService 用 trade-derived 值填入
            InterestIncome: s?.TaxInterestIncome ?? 0m,
            RentalExpense: s?.TaxRentalExpense ?? 0m,
            UseItemized: s?.TaxUseItemizedDeduction ?? false,
            ItemizedAmount: s?.TaxItemizedDeductionAmount ?? 0m,
            DividendSeparate: s?.TaxDividendSeparate ?? false);

        var amtInputs = new AmtCalculationParameters(
            RegularTaxableIncome: s?.AmtRegularTaxableIncome ?? 0m,
            RegularIncomeTax: s?.AmtRegularIncomeTax ?? 0m,
            InsuranceDeathProceeds: s?.AmtInsuranceDeathProceeds ?? 0m,
            InsuranceNonDeathProceeds: s?.AmtInsuranceNonDeathProceeds ?? 0m,
            UnlistedSecurityGains: s?.AmtUnlistedSecurityGains ?? 0m,
            NonCashDonation: s?.AmtNonCashDonation ?? 0m,
            PrivateFundGains: s?.AmtPrivateFundGains ?? 0m,
            OverseasTaxCredit: s?.AmtOverseasTaxCredit ?? 0m);

        return (filing, amtInputs);
    }

    private string FormatAmount(decimal value) =>
        _currency?.FormatAmount(value) ?? $"NT${value:N0}";

    private string FormatSignedAmount(decimal value) =>
        _currency?.FormatSigned(value)
        ?? (value >= 0 ? $"+NT${Math.Abs(value):N0}" : $"-NT${Math.Abs(value):N0}");

    private void OnCurrencyChanged() => RaiseDisplayStrings();

    private void OnLanguageChanged(object? sender, EventArgs e) => RaiseDisplayStrings();

    private void OnSettingsChanged()
    {
        if (HasReport)
            _ = LoadAsync();
    }

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

    private string GetString(string key, string fallback) =>
        _localization?.Get(key, fallback) ?? fallback;


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
