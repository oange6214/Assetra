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
    private PerformanceResult? _performance;

    [ObservableProperty]
    private RiskMetrics? _risk;

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

        var today = DateTime.Today;
        _year = today.Year;
        _month = today.Month;

        if (_currency is not null)
            _currency.CurrencyChanged += OnCurrencyChanged;
        if (_localization is not null)
            _localization.LanguageChanged += OnLanguageChanged;
        if (_appSettings is not null)
            _appSettings.Changed += OnSettingsChanged;
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
        await LoadRiskAsync().ConfigureAwait(true);
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

    private async Task ExportAsync(string target, string formatStr)
    {
        if (_exportService is null)
        {
            ExportStatus = GetString("Reports.Export.Status.Unavailable", "匯出服務目前不可用。");
            return;
        }
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
            var date = DateOnly.FromDateTime(trade.TradeDate);
            if (date < period.Start || date > period.End) continue;

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
}
