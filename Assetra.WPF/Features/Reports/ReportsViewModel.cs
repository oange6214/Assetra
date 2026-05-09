using System.Collections.ObjectModel;
using System.Net.Http;
using Assetra.Application.Fx;
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
    private readonly ITradeRepository? _trades;
    private readonly IPortfolioRepository? _portfolio;
    private readonly IMultiCurrencyValuationService? _fx;
    private readonly IXirrCalculator? _xirrCalculator;
    private readonly ITimeWeightedReturnCalculator? _twrCalculator;
    private readonly IMoneyWeightedReturnCalculator? _mwrCalculator;
    private readonly IPnlAttributionService? _attribution;
    private readonly IBenchmarkComparisonService? _benchmark;
    private readonly IVolatilityCalculator? _volatility;
    private readonly IDrawdownCalculator? _drawdown;
    private readonly ISharpeRatioCalculator? _sharpe;
    private readonly IConcentrationAnalyzer? _concentration;
    private readonly IPortfolioSnapshotRepository? _snapshots;
    private readonly IAppSettingsService? _appSettings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPerformance))]
    [NotifyPropertyChangedFor(nameof(PerformanceAttributionRows))]
    [NotifyPropertyChangedFor(nameof(HasPerformanceAttribution))]
    private PerformanceResult? _performance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRisk))]
    [NotifyPropertyChangedFor(nameof(RiskTopHoldingRows))]
    [NotifyPropertyChangedFor(nameof(HasRiskTopHoldings))]
    private RiskMetrics? _risk;

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
        IXirrCalculator? xirrCalculator = null,
        ITimeWeightedReturnCalculator? twrCalculator = null,
        IMoneyWeightedReturnCalculator? mwrCalculator = null,
        IPnlAttributionService? attribution = null,
        IBenchmarkComparisonService? benchmark = null,
        IVolatilityCalculator? volatility = null,
        IDrawdownCalculator? drawdown = null,
        ISharpeRatioCalculator? sharpe = null,
        IConcentrationAnalyzer? concentration = null,
        IPortfolioSnapshotRepository? snapshots = null,
        IAppSettingsService? appSettings = null)
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
        _xirrCalculator = xirrCalculator;
        _twrCalculator = twrCalculator;
        _mwrCalculator = mwrCalculator;
        _attribution = attribution;
        _benchmark = benchmark;
        _volatility = volatility;
        _drawdown = drawdown;
        _sharpe = sharpe;
        _concentration = concentration;
        _snapshots = snapshots;
        _appSettings = appSettings;
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
    public bool HasPerformance => Performance is not null;
    public bool HasRisk => Risk is not null;
    public bool HasPerformanceAttribution => PerformanceAttributionRows.Count > 0;
    public bool HasRiskTopHoldings => RiskTopHoldingRows.Count > 0;

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

    public IReadOnlyList<PerformanceAttributionRowViewModel> PerformanceAttributionRows =>
        Performance?.Attribution
            .Select(row => new PerformanceAttributionRowViewModel(
                LocalizeAttributionLabel(row.Label),
                FormatSignedAmount(row.Amount),
                row.Amount >= 0m))
            .ToArray()
        ?? [];

    public IReadOnlyList<RiskTopHoldingRowViewModel> RiskTopHoldingRows =>
        Risk?.TopHoldings
            .Select(row => new RiskTopHoldingRowViewModel(
                row.Label,
                FormatAmount(row.MarketValue),
                $"{row.Weight:P2}",
                row.Weight))
            .ToArray()
        ?? [];

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
                TaxSummary = Assetra.Application.Tax.TaxCalculationService.CalculateForYear(Year, allTrades);

                // Multi-year comparison: scan all trade-bearing years (CashDividend
                // + Sell-with-PnL only) and build one row per year. Rebuild the
                // collection in-place so existing XAML bindings stay attached.
                var taxYears = allTrades
                    .Where(t => t.Type == Assetra.Core.Models.TradeType.CashDividend
                             || (t.Type == Assetra.Core.Models.TradeType.Sell && t.RealizedPnl is not null))
                    .Select(t => t.TradeDate.Year)
                    .Distinct()
                    .OrderByDescending(y => y)
                    .ToList();

                _multiYearTaxRows.Clear();
                foreach (var y in taxYears)
                {
                    var s = Assetra.Application.Tax.TaxCalculationService.CalculateForYear(y, allTrades);
                    _multiYearTaxRows.Add(TaxYearRowViewModel.FromSummary(s));
                }
                OnPropertyChanged(nameof(HasMultiYearTax));

                // AMT calculation for the focal year — pulls parameters from AppSettings
                // (Exemption / Rate / RegularTaxableIncome / RegularIncomeTax). Computed
                // even when 海外所得 below threshold so the UI can show "未達門檻" state.
                if (TaxSummary is not null && _appSettings is not null)
                {
                    var s = _appSettings.Current;
                    var parameters = new AmtCalculationParameters(
                        Exemption: s.AmtExemption,
                        Rate: s.AmtRate,
                        RegularTaxableIncome: s.AmtRegularTaxableIncome,
                        RegularIncomeTax: s.AmtRegularIncomeTax);
                    AmtResult = Assetra.Application.Tax.TaxCalculationService.ComputeAmtLiability(TaxSummary, parameters);
                }
                else
                {
                    AmtResult = null;
                }
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

        try
        {
            await LoadPerformanceAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Performance = null;
            detailErrors.Add(ex.Message);
        }

        try
        {
            await LoadRiskAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Risk = null;
            detailErrors.Add(ex.Message);
        }

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
        Performance = null;
        Risk = null;
    }

    private async Task LoadRiskAsync()
    {
        var perfPeriod = PerformancePeriod.Month(Year, Month);

        IReadOnlyList<(DateOnly Date, decimal Value)> series = Array.Empty<(DateOnly, decimal)>();
        if (_snapshots is not null)
        {
            var raw = await _snapshots.GetSnapshotsAsync(perfPeriod.Start, perfPeriod.End).ConfigureAwait(true);
            series = raw.Select(s => (s.SnapshotDate, s.MarketValue)).ToList();
        }

        var vol = _volatility?.ComputeAnnualized(series);
        var mdd = _drawdown?.ComputeMaxDrawdown(series);
        var twr = Performance?.Twr ?? Performance?.Mwr;
        var sharpe = _sharpe?.Compute(twr, vol, riskFreeRate: 0.02m);

        IReadOnlyList<ConcentrationBucket> top = Array.Empty<ConcentrationBucket>();
        decimal? hhi = null;
        if (_concentration is not null)
        {
            top = await _concentration.AnalyzeAsync().ConfigureAwait(true);
            hhi = await _concentration.ComputeHhiAsync().ConfigureAwait(true);
        }

        Risk = new RiskMetrics(vol, mdd, sharpe, hhi, top);
    }

    private async Task LoadPerformanceAsync()
    {
        if (_mwrCalculator is null && _attribution is null && _twrCalculator is null && _xirrCalculator is null) return;
        var perfPeriod = PerformancePeriod.Month(Year, Month);
        var xirr = await ComputeXirrAsync(perfPeriod).ConfigureAwait(true);
        var twr = await ComputeTwrAsync(perfPeriod).ConfigureAwait(true);
        var mwr = _mwrCalculator is null ? null : await _mwrCalculator.ComputeAsync(perfPeriod).ConfigureAwait(true);
        var buckets = _attribution is null
            ? (IReadOnlyList<AttributionBucket>)Array.Empty<AttributionBucket>()
            : await _attribution.ComputeAsync(perfPeriod).ConfigureAwait(true);
        decimal? bench = null;
        var benchmarkSymbol = _appSettings?.Current.BenchmarkSymbol ?? "0050.TW";
        if (_benchmark is not null && !string.IsNullOrWhiteSpace(benchmarkSymbol))
        {
            try
            {
                bench = await _benchmark.ComputeBenchmarkTwrAsync(benchmarkSymbol, perfPeriod).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"Benchmark TWR failed for {benchmarkSymbol}: {ex.Message}");
                bench = null;
            }
        }
        Performance = new PerformanceResult(perfPeriod, Xirr: xirr, Twr: twr, Mwr: mwr, BenchmarkTwr: bench, Attribution: buckets);
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
        OnPropertyChanged(nameof(PerformanceAttributionRows));
        OnPropertyChanged(nameof(RiskTopHoldingRows));
    }

    private string GetString(string key, string fallback) =>
        _localization?.Get(key, fallback) ?? fallback;

    private string LocalizeAttributionLabel(string label) =>
        label switch
        {
            "Realized" => GetString("Reports.Performance.Attribution.Realized", "已實現損益"),
            "Dividend" => GetString("Reports.Performance.Attribution.Dividend", "股利"),
            "Commission" => GetString("Reports.Performance.Attribution.Commission", "手續費"),
            "Unrealized Δ" => GetString("Reports.Performance.Attribution.Unrealized", "未實現變動"),
            _ => label,
        };

    private async Task<decimal?> ComputeXirrAsync(PerformancePeriod period)
    {
        if (_xirrCalculator is null || _trades is null || _snapshots is null)
            return null;

        var entryCurrencyMap = await BuildEntryCurrencyMapAsync().ConfigureAwait(true);
        var flows = BuildPerformanceFlows(await _trades.GetAllAsync().ConfigureAwait(true), period, entryCurrencyMap);
        flows = await ConvertFlowsToBaseAsync(flows).ConfigureAwait(true);
        if (flows is null) return null;

        var startSnap = await _snapshots.GetSnapshotAsync(period.Start).ConfigureAwait(true);
        var endSnap = await _snapshots.GetSnapshotAsync(period.End).ConfigureAwait(true);
        var startMv = await ConvertSnapshotMarketValueAsync(startSnap).ConfigureAwait(true);
        if (startSnap is not null && startMv is null) return null;
        var endMv = await ConvertSnapshotMarketValueAsync(endSnap).ConfigureAwait(true);
        if (endSnap is not null && endMv is null) return null;

        var withSynthetic = new List<CashFlow>(flows);
        if (startMv is > 0m)
            withSynthetic.Insert(0, new CashFlow(period.Start, -startMv.Value, GetBaseCurrency()));
        if (endMv is > 0m)
            withSynthetic.Add(new CashFlow(period.End, endMv.Value, GetBaseCurrency()));

        return _xirrCalculator.Compute(withSynthetic);
    }

    private async Task<decimal?> ComputeTwrAsync(PerformancePeriod period)
    {
        if (_twrCalculator is null || _trades is null || _snapshots is null)
            return null;

        var rawSnapshots = await _snapshots.GetSnapshotsAsync(period.Start, period.End).ConfigureAwait(true);
        var valuations = new List<(DateOnly Date, decimal Value)>(rawSnapshots.Count);
        foreach (var snap in rawSnapshots.OrderBy(s => s.SnapshotDate))
        {
            var converted = await ConvertSnapshotMarketValueAsync(snap).ConfigureAwait(true);
            if (converted is null) return null;
            valuations.Add((snap.SnapshotDate, converted.Value));
        }

        if (valuations.Count < 2) return null;

        var entryCurrencyMap = await BuildEntryCurrencyMapAsync().ConfigureAwait(true);
        var flows = BuildPerformanceFlows(await _trades.GetAllAsync().ConfigureAwait(true), period, entryCurrencyMap);
        flows = await ConvertFlowsToBaseAsync(flows).ConfigureAwait(true);
        if (flows is null) return null;

        return _twrCalculator.Compute(valuations, flows);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> BuildEntryCurrencyMapAsync()
    {
        if (_portfolio is null) return new Dictionary<Guid, string>();
        var entries = await _portfolio.GetEntriesAsync().ConfigureAwait(true);
        return entries.ToDictionary(
            e => e.Id,
            e => string.IsNullOrWhiteSpace(e.Currency) ? string.Empty : e.Currency);
    }

    private async Task<List<CashFlow>?> ConvertFlowsToBaseAsync(List<CashFlow> flows)
    {
        var baseCurrency = GetBaseCurrency();
        if (_fx is null || string.IsNullOrWhiteSpace(baseCurrency)) return flows;
        var converted = await MultiCurrencyCashFlowConverter.ConvertAllAsync(flows, baseCurrency, _fx).ConfigureAwait(true);
        return converted is null ? null : converted.ToList();
    }

    private async Task<decimal?> ConvertSnapshotMarketValueAsync(PortfolioDailySnapshot? snap)
    {
        if (snap is null) return null;

        var baseCurrency = GetBaseCurrency();
        if (_fx is null || string.IsNullOrWhiteSpace(baseCurrency)) return snap.MarketValue;

        var snapshotCurrency = string.IsNullOrWhiteSpace(snap.Currency) ? "TWD" : snap.Currency;
        if (string.Equals(snapshotCurrency, baseCurrency, StringComparison.OrdinalIgnoreCase))
            return snap.MarketValue;

        return await _fx.ConvertAsync(snap.MarketValue, snapshotCurrency, baseCurrency, snap.SnapshotDate).ConfigureAwait(true);
    }

    private string? GetBaseCurrency()
    {
        var baseCurrency = _appSettings?.Current.BaseCurrency;
        return string.IsNullOrWhiteSpace(baseCurrency) ? null : baseCurrency;
    }

    private static List<CashFlow> BuildPerformanceFlows(
        IReadOnlyList<Trade> trades,
        PerformancePeriod period,
        IReadOnlyDictionary<Guid, string> entryCurrency)
    {
        var flows = new List<CashFlow>();
        foreach (var trade in trades)
        {
            var date = PerformancePeriod.ToPeriodDate(trade.TradeDate);
            if (!period.Contains(date)) continue;

            var amount = trade.Type switch
            {
                TradeType.Buy => -((decimal)trade.Quantity * trade.Price + (trade.Commission ?? 0m)),
                TradeType.Sell => (decimal)trade.Quantity * trade.Price - (trade.Commission ?? 0m),
                TradeType.CashDividend => trade.CashAmount ?? (decimal)trade.Quantity * trade.Price,
                _ => 0m,
            };
            if (amount == 0m) continue;

            string? currency = null;
            if (trade.PortfolioEntryId is { } entryId
                && entryCurrency.TryGetValue(entryId, out var entryCcy)
                && !string.IsNullOrWhiteSpace(entryCcy))
            {
                currency = entryCcy;
            }

            flows.Add(new CashFlow(date, amount, currency));
        }
        return flows;
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

    public sealed record PerformanceAttributionRowViewModel(
        string Label,
        string AmountDisplay,
        bool IsPositive);

    public sealed record RiskTopHoldingRowViewModel(
        string Label,
        string MarketValueDisplay,
        string WeightDisplay,
        decimal Weight)
    {
        public decimal WeightPercent => Weight * 100m;
    }
}
