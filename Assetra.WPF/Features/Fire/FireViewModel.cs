using System.Collections.ObjectModel;
using System.Globalization;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Fire;
using Assetra.Core.Models;
using Assetra.Core.Models.Fire;
using Assetra.WPF.Features.PortfolioGroups;
using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Assetra.WPF.Features.Fire;

public sealed partial class FireViewModel : ObservableObject
{
    private readonly IFireCalculatorService _calculator;
    private readonly IFinancialGoalRepository _goals;
    private readonly ISnackbarService? _snackbar;
    private readonly ILocalizationService? _localization;
    private readonly IGroupBalanceQueryService? _groupBalance;
    private readonly IAppNetWorthProvider? _appNetWorthProvider;
    private readonly IFirePlanningService? _planningService;
    private readonly IFireScenarioRepository? _scenarioRepository;
    private readonly IFireDrawdownService? _drawdownService;
    private readonly IFireMonteCarloService? _monteCarloService;
    private bool _hasLoadedAppNetWorth;

    // 退休成功率模擬政策：固定 15% 年化波動（使用者選定，覆寫 FireMonteCarloService 預設的 12%），
    // 並用固定亂數種子讓相同情境每次都顯示一致的成功率（避免畫面數字跳動）。
    private const decimal MonteCarloReturnStdDev = 0.15m;
    private const int MonteCarloSeed = 8675309;
    private bool _isSettingCurrentNetWorthFromSource;
    private bool _isCurrentNetWorthUserEdited;

    /// <summary>
    /// Portfolio-Groups-Refactor P6 — 共用 group catalog。null = 功能未啟用，
    /// XAML 上的 group 選擇器隱藏，FIRE 仍可用全域淨資產計算。
    /// </summary>
    public PortfolioGroupCatalog? GroupCatalog { get; }

    // P3.9 — 排除 IsSystem default group (見 PortfolioViewModel.HasPortfolioGroups 的完整理由)。
    public bool HasPortfolioGroups => GroupCatalog?.Groups.Any(g => !g.IsSystem) == true;

    /// <summary>
    /// 使用者在 FIRE 頁選的 group。Set 時若 catalog 注入了 <see cref="IGroupBalanceQueryService"/>，
    /// 自動把該 group 的累計淨值填入 <see cref="CurrentNetWorth"/>，給使用者一個合理的起始值。
    /// null = 用全域 / 手動輸入。
    /// </summary>
    [ObservableProperty]
    private PortfolioGroup? _selectedGroup;

    partial void OnSelectedGroupChanged(PortfolioGroup? value) => _ = ApplyGroupNetWorthAsync(value);

    private async Task ApplyGroupNetWorthAsync(PortfolioGroup? group)
    {
        if (group is null || _groupBalance is null)
            return;
        try
        {
            var nv = await _groupBalance.ComputeNetValueAsync(group.Id).ConfigureAwait(true);
            SetCurrentNetWorthFromSource(nv);
        }
        catch
        {
            // Silent failure — user can still type the value manually.
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FireProgressDisplay))]
    [NotifyPropertyChangedFor(nameof(FireProgressValue))]
    private string _currentNetWorth = "1,000,000";

    partial void OnCurrentNetWorthChanged(string value)
    {
        if (!_isSettingCurrentNetWorthFromSource)
            _isCurrentNetWorthUserEdited = true;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnnualExpensesMonthlyAverageDisplay))]
    [NotifyPropertyChangedFor(nameof(FireFormulaDisplay))]
    private string _annualExpenses = "600,000";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalContributionDisplay))]
    [NotifyPropertyChangedFor(nameof(AnnualSavingsMonthlyAverageDisplay))]
    private string _annualSavings = "300,000";
    [ObservableProperty] private string _expectedAnnualReturn = "0.05";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FireFormulaDisplay))]
    private string _withdrawalRate = "0.04";
    [ObservableProperty] private string _currentAge = string.Empty;
    [ObservableProperty] private string _lifeExpectancyAge = "90";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RetirementAnnualExpensesMonthlyAverageDisplay))]
    private string _retirementAnnualExpenses = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InflationRateHintDisplay))]
    private string _inflationRate = "0.02";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBasicMode))]
    private bool _isAdvancedMode;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlanningWarnings))]
    [NotifyPropertyChangedFor(nameof(HasDrawdownPath))]
    [NotifyPropertyChangedFor(nameof(RequiredMonthlySavingsDisplay))]
    [NotifyPropertyChangedFor(nameof(MonteCarloSuccessRateDisplay))]
    private FirePlanningProjection? _planningResult;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FireProgressDisplay))]
    [NotifyPropertyChangedFor(nameof(FireProgressValue))]
    private decimal _fireNumber;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FireYearDisplay))]
    [NotifyPropertyChangedFor(nameof(TotalContributionDisplay))]
    private string _yearsToFire = string.Empty;

    [ObservableProperty] private decimal _projectedNetWorthAtFire;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FireProgressDisplay))]
    [NotifyPropertyChangedFor(nameof(TotalContributionDisplay))]
    [NotifyCanExecuteChangedFor(nameof(SaveToGoalsCommand))]
    private bool _hasCalculatedResult;

    /// <summary>
    /// P2.17 T01 — 預估自由年份。從今天加 YearsToFire 年後的西元年份顯示。
    /// YearsToFire 是 string ("—" or "N") — 沒有有效年數時回傳「—」。
    /// </summary>
    public string FireYearDisplay
    {
        get
        {
            if (!int.TryParse(YearsToFire, NumberStyles.Integer, CultureInfo.InvariantCulture, out var years) || years < 0)
                return "—";
            return DateTime.Today.AddYears(years).Year.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// P2.17 T01 — 進度 = CurrentNetWorth / FireNumber, clamp 0-100。
    /// FireNumber=0 或 CurrentNetWorth 不可 parse 時回 0。
    /// </summary>
    public decimal FireProgressValue
    {
        get
        {
            if (FireNumber <= 0m)
                return 0m;
            if (!TryParseDecimal(CurrentNetWorth, out var nw))
                return 0m;
            var pct = nw / FireNumber * 100m;
            return Math.Clamp(pct, 0m, 100m);
        }
    }

    /// <summary>P2.17 T01 — 進度顯示字串「34%」格式。</summary>
    public string FireProgressDisplay =>
        HasCalculatedResult ? FireProgressValue.ToString("F0", CultureInfo.InvariantCulture) + "%" : "—";

    public string AnnualExpensesMonthlyAverageDisplay => MonthlyAverageDisplay(AnnualExpenses);

    public string AnnualSavingsMonthlyAverageDisplay => MonthlyAverageDisplay(AnnualSavings);

    public string RetirementAnnualExpensesMonthlyAverageDisplay => MonthlyAverageDisplay(RetirementAnnualExpenses);

    public string InflationRateHintDisplay
    {
        get
        {
            if (!TryParseDecimal(InflationRate, out var rate))
                return L("Fire.Advanced.InflationRate.HintExample", "例如 0.02 = 每年 2%");

            return string.Format(
                CultureInfo.InvariantCulture,
                L("Fire.Advanced.InflationRate.HintFormat", "{0} = 每年 {1:0.##}%"),
                FormatRateInput(rate),
                rate * 100m);
        }
    }

    public bool IsBasicMode
    {
        get => !IsAdvancedMode;
        set => IsAdvancedMode = !value;
    }

    public bool HasPlanningWarnings => PlanningResult?.Warnings.Count > 0;

    public bool HasDrawdownPath => PlanningResult?.DrawdownPath.Count > 0;

    public string RequiredMonthlySavingsDisplay =>
        PlanningResult is null
            ? "—"
            : PlanningResult.RequiredMonthlySavings.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>
    /// 退休資產撐到預期壽命的 Monte Carlo 成功率，格式「92%」。
    /// 只有進階模式填了年齡才會算出來；未計算時回「—」。
    /// </summary>
    public string MonteCarloSuccessRateDisplay =>
        PlanningResult?.MonteCarloSuccessRate is decimal rate
            ? (rate * 100m).ToString("F0", CultureInfo.InvariantCulture) + "%"
            : "—";

    public string FireFormulaDisplay
    {
        get
        {
            if (!TryParseDecimal(AnnualExpenses, out var expenses)
                || !TryParseDecimal(WithdrawalRate, out var withdrawalRate)
                || expenses <= 0m
                || withdrawalRate <= 0m)
            {
                return "年支出 ÷ 安全提領率 = —";
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{expenses:N0} ÷ {withdrawalRate * 100m:F2}% = {expenses / withdrawalRate:N0}");
        }
    }

    /// <summary>
    /// P2.17 T01 — 累計投入金額 = AnnualSavings × YearsToFire。
    /// 給使用者「我大概要投入多少」的整體感。
    /// </summary>
    public string TotalContributionDisplay
    {
        get
        {
            if (!HasCalculatedResult)
                return "—";
            if (!int.TryParse(YearsToFire, NumberStyles.Integer, CultureInfo.InvariantCulture, out var years) || years <= 0)
                return "—";
            if (!TryParseDecimal(AnnualSavings, out var sav) || sav <= 0m)
                return "—";
            var total = sav * years;
            return total.ToString("N0", CultureInfo.InvariantCulture);
        }
    }

    private readonly ObservableCollection<FireWealthPoint> _wealthPath = [];
    public ReadOnlyObservableCollection<FireWealthPoint> WealthPath { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWealthPathChart))]
    private ISeries[] _wealthPathChartSeries = [];

    public Axis[] WealthPathChartXAxes { get; } =
    [
        new Axis
        {
            TextSize = 10,
            Labeler = value => value.ToString("N0", CultureInfo.InvariantCulture),
        },
    ];

    public Axis[] WealthPathChartYAxes { get; } =
    [
        new Axis
        {
            Position = AxisPosition.End,
            TextSize = 10,
            Labeler = value => value.ToString("N0", CultureInfo.InvariantCulture),
        },
    ];

    public bool HasWealthPathChart => WealthPathChartSeries.Length > 0;

    public ObservableCollection<FireScenarioRowViewModel> Scenarios { get; } = [];

    [ObservableProperty]
    private FireScenarioRowViewModel? _selectedScenario;

    [ObservableProperty]
    private bool _isCreateScenarioOpen;

    [ObservableProperty]
    private string _scenarioDraftName = string.Empty;

    [ObservableProperty]
    private string? _scenarioDraftError;

    partial void OnSelectedScenarioChanged(FireScenarioRowViewModel? value)
    {
        if (value is null)
            return;

        var shouldRecalculate = HasCalculatedResult;
        ApplyScenarioToInputs(value.Scenario);
        if (shouldRecalculate)
        {
            ClearCalculatedResult();
            _ = CalculatePlanningCommand.ExecuteAsync(null);
        }
    }

    partial void OnScenarioDraftNameChanged(string value)
    {
        if (ScenarioDraftError is not null)
            ScenarioDraftError = null;
    }

    public FireViewModel(
        IFireCalculatorService calculator,
        IFinancialGoalRepository goals,
        ISnackbarService? snackbar = null,
        ILocalizationService? localization = null,
        PortfolioGroupCatalog? groupCatalog = null,
        IGroupBalanceQueryService? groupBalance = null,
        IAppNetWorthProvider? appNetWorthProvider = null,
        IFirePlanningService? planningService = null,
        IFireScenarioRepository? scenarioRepository = null,
        IFireDrawdownService? drawdownService = null,
        IFireMonteCarloService? monteCarloService = null)
    {
        ArgumentNullException.ThrowIfNull(calculator);
        ArgumentNullException.ThrowIfNull(goals);
        _calculator = calculator;
        _goals = goals;
        _snackbar = snackbar;
        _localization = localization;
        GroupCatalog = groupCatalog;
        _groupBalance = groupBalance;
        _appNetWorthProvider = appNetWorthProvider;
        _planningService = planningService;
        _scenarioRepository = scenarioRepository;
        _drawdownService = drawdownService;
        _monteCarloService = monteCarloService;
        WealthPath = new ReadOnlyObservableCollection<FireWealthPoint>(_wealthPath);
    }

    /// <summary>
    /// 外部呼叫（FireView Loaded）以確保 group catalog 預先載入給 ComboBox 用。
    /// </summary>
    public Task EnsureGroupCatalogLoadedAsync()
    {
        var t = GroupCatalog?.EnsureLoadedAsync() ?? Task.CompletedTask;
        return t.ContinueWith(_ => OnPropertyChanged(nameof(HasPortfolioGroups)), TaskScheduler.FromCurrentSynchronizationContext());
    }

    public async Task LoadCurrentNetWorthAsync(CancellationToken ct = default)
    {
        if (_appNetWorthProvider is null
            || _hasLoadedAppNetWorth
            || _isCurrentNetWorthUserEdited
            || SelectedGroup is not null
            || SelectedScenario is not null)
        {
            return;
        }

        try
        {
            var netWorth = await _appNetWorthProvider.GetCurrentNetWorthAsync(ct).ConfigureAwait(true);
            SetCurrentNetWorthFromSource(netWorth);
            _hasLoadedAppNetWorth = true;
        }
        catch
        {
            // Silent failure — FIRE remains usable with the manual input.
        }
    }

    [RelayCommand]
    public async Task LoadScenariosAsync(CancellationToken ct = default)
    {
        if (_scenarioRepository is null)
            return;

        IReadOnlyList<FireScenario> scenarios;
        try
        {
            scenarios = await _scenarioRepository.GetAllAsync(ct).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        Scenarios.Clear();
        foreach (var scenario in scenarios
            .OrderByDescending(s => s.IsDefault)
            .ThenBy(s => s.Name, StringComparer.CurrentCulture))
        {
            Scenarios.Add(new FireScenarioRowViewModel(scenario));
        }

        SelectedScenario = Scenarios.FirstOrDefault(s => s.IsDefault) ?? Scenarios.FirstOrDefault();
    }

    [RelayCommand]
    private Task CreateScenarioAsync()
    {
        ScenarioDraftName = GenerateUniqueScenarioName(L("Fire.Scenario.DefaultName", "Base"));
        ScenarioDraftError = null;
        IsCreateScenarioOpen = true;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void CancelCreateScenario()
    {
        IsCreateScenarioOpen = false;
        ScenarioDraftError = null;
    }

    [RelayCommand]
    private async Task ConfirmCreateScenarioAsync()
    {
        if (_scenarioRepository is null)
            return;
        if (!TryValidateScenarioDraftName(out var name) || !TryBuildScenario(null, out var scenario))
            return;

        scenario = scenario with { Name = name };
        await SaveScenarioAndReloadAsync(scenario, Array.Empty<FireCashFlowEvent>()).ConfigureAwait(true);
        IsCreateScenarioOpen = false;
    }

    [RelayCommand]
    private async Task SaveScenarioAsync()
    {
        if (_scenarioRepository is null || !TryBuildScenario(SelectedScenario?.Scenario, out var scenario))
            return;

        await SaveScenarioAndReloadAsync(scenario, Array.Empty<FireCashFlowEvent>()).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DuplicateScenarioAsync()
    {
        if (_scenarioRepository is null || SelectedScenario is null)
            return;

        var now = DateTimeOffset.Now;
        var copy = SelectedScenario.Scenario with
        {
            Id = Guid.NewGuid(),
            Name = GenerateUniqueScenarioName(SelectedScenario.Name + " Copy"),
            IsDefault = false,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var events = await _scenarioRepository.GetCashFlowEventsAsync(SelectedScenario.Id).ConfigureAwait(true);
        var copiedEvents = events
            .Select(e => e with { Id = Guid.NewGuid(), ScenarioId = copy.Id })
            .ToArray();

        await SaveScenarioAndReloadAsync(copy, copiedEvents).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task DeleteScenarioAsync()
    {
        if (_scenarioRepository is null || SelectedScenario is null)
            return;

        await _scenarioRepository.DeleteAsync(SelectedScenario.Id).ConfigureAwait(true);
        await LoadScenariosAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SetDefaultScenarioAsync()
    {
        if (_scenarioRepository is null || SelectedScenario is null)
            return;

        var scenario = SelectedScenario.Scenario with
        {
            IsDefault = true,
            UpdatedAt = DateTimeOffset.Now,
        };

        var events = await _scenarioRepository.GetCashFlowEventsAsync(scenario.Id).ConfigureAwait(true);
        await SaveScenarioAndReloadAsync(scenario, events).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task CalculatePlanningAsync()
    {
        if (_planningService is null || !TryBuildScenario(SelectedScenario?.Scenario, out var scenario))
        {
            Calculate();
            return;
        }

        var events = _scenarioRepository is null || scenario.Id == Guid.Empty
            ? Array.Empty<FireCashFlowEvent>()
            : await _scenarioRepository.GetCashFlowEventsAsync(scenario.Id).ConfigureAwait(true);

        try
        {
            PlanningResult = _planningService.Project(scenario, events, DateTime.Today.Year);

            if (scenario.CurrentAge.HasValue && scenario.LifeExpectancyAge.HasValue)
            {
                var currentAge = scenario.CurrentAge.Value;
                var lifeExpectancyAge = scenario.LifeExpectancyAge.Value;

                IReadOnlyList<FireDrawdownPoint> drawdownPath = PlanningResult.DrawdownPath;
                IReadOnlyList<FireProjectionWarning> warnings = PlanningResult.Warnings;
                decimal? successRate = PlanningResult.MonteCarloSuccessRate;

                if (_drawdownService is not null)
                {
                    var drawdown = _drawdownService.ProjectDrawdown(
                        PlanningResult.ProjectedNetWorthAtFire,
                        scenario.RetirementAnnualExpenses ?? scenario.AnnualExpenses,
                        scenario.ExpectedAnnualReturn,
                        currentAge,
                        lifeExpectancyAge);
                    drawdownPath = drawdown.DrawdownPath;
                    warnings = warnings.Concat(drawdown.Warnings).ToArray();
                }

                // Monte Carlo 退休成功率：退休從「達成 FIRE 的年齡」(currentAge + YearsToFire)
                // 起算到預期壽命，起始餘額用達成 FIRE 時的淨資產。無法達成 FIRE（YearsToFire 為
                // null）或退休年數 ≤ 0 時不估算，成功率維持 null。
                if (_monteCarloService is not null && PlanningResult.YearsToFire is int yearsToFire)
                {
                    var retirementYears = lifeExpectancyAge - currentAge - yearsToFire;
                    if (retirementYears > 0)
                    {
                        successRate = _monteCarloService.EstimateRetirementSuccess(
                            scenario,
                            PlanningResult.ProjectedNetWorthAtFire,
                            retirementYears,
                            randomSeed: MonteCarloSeed,
                            annualReturnStdDev: MonteCarloReturnStdDev).SuccessRate;
                    }
                }

                PlanningResult = PlanningResult with
                {
                    DrawdownPath = drawdownPath,
                    Warnings = warnings,
                    MonteCarloSuccessRate = successRate,
                };
            }

            FireNumber = PlanningResult.RequiredAssets;
            YearsToFire = PlanningResult.YearsToFire?.ToString(CultureInfo.InvariantCulture) ?? "—";
            ProjectedNetWorthAtFire = PlanningResult.ProjectedNetWorthAtFire;
            HasCalculatedResult = true;

            _wealthPath.Clear();
            foreach (var point in PlanningResult.AccumulationPath)
                _wealthPath.Add(new FireWealthPoint(point.Year, point.NetWorth));
            RefreshWealthPathChart();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            ErrorMessage = ToFriendlyError(ex.ParamName);
        }
    }

    private void SetCurrentNetWorthFromSource(decimal value)
    {
        _isSettingCurrentNetWorthFromSource = true;
        try
        {
            CurrentNetWorth = value.ToString("N0", CultureInfo.InvariantCulture);
        }
        finally
        {
            _isSettingCurrentNetWorthFromSource = false;
        }
    }

    private void ApplyScenarioToInputs(FireScenario scenario)
    {
        IsAdvancedMode = scenario.Mode == FireScenarioMode.Advanced;
        if (scenario.CurrentNetWorthOverride.HasValue)
            SetCurrentNetWorthFromSource(scenario.CurrentNetWorthOverride.Value);
        AnnualExpenses = FormatMoneyInput(scenario.AnnualExpenses);
        AnnualSavings = FormatMoneyInput(scenario.AnnualSavings);
        ExpectedAnnualReturn = FormatRateInput(scenario.ExpectedAnnualReturn);
        WithdrawalRate = FormatRateInput(scenario.WithdrawalRate);
        CurrentAge = scenario.CurrentAge?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        LifeExpectancyAge = scenario.LifeExpectancyAge?.ToString(CultureInfo.InvariantCulture)
            ?? (scenario.Mode == FireScenarioMode.Advanced ? "90" : string.Empty);
        RetirementAnnualExpenses = scenario.RetirementAnnualExpenses.HasValue
            ? FormatMoneyInput(scenario.RetirementAnnualExpenses.Value)
            : string.Empty;
        InflationRate = scenario.InflationRate.HasValue
            ? FormatRateInput(scenario.InflationRate.Value)
            : (scenario.Mode == FireScenarioMode.Advanced ? "0.02" : string.Empty);
    }

    private void ClearCalculatedResult()
    {
        PlanningResult = null;
        FireNumber = 0m;
        YearsToFire = "—";
        ProjectedNetWorthAtFire = 0m;
        HasCalculatedResult = false;
        _wealthPath.Clear();
        RefreshWealthPathChart();
    }

    private bool TryBuildScenario(FireScenario? existing, out FireScenario scenario)
    {
        scenario = null!;
        ErrorMessage = null;

        if (!TryParseDecimal(CurrentNetWorth, out var netWorth))
        {
            ErrorMessage = L("Fire.Error.NetWorthInvalid", "目前淨資產格式錯誤");
            return false;
        }
        if (!TryParseDecimal(AnnualExpenses, out var expenses))
        {
            ErrorMessage = L("Fire.Error.ExpensesInvalid", "年支出格式錯誤");
            return false;
        }
        if (!TryParseDecimal(AnnualSavings, out var savings))
        {
            ErrorMessage = L("Fire.Error.SavingsInvalid", "年儲蓄格式錯誤");
            return false;
        }
        if (!TryParseDecimal(ExpectedAnnualReturn, out var expectedReturn))
        {
            ErrorMessage = L("Fire.Error.ReturnInvalid", "預期報酬率格式錯誤");
            return false;
        }
        if (!TryParseDecimal(WithdrawalRate, out var withdrawalRate))
        {
            ErrorMessage = L("Fire.Error.WithdrawalInvalid", "提領率格式錯誤");
            return false;
        }

        int? currentAge = null;
        int? lifeExpectancyAge = null;
        decimal? retirementAnnualExpenses = null;
        decimal? inflationRate = null;
        if (IsAdvancedMode)
        {
            if (!TryParseOptionalInt(CurrentAge, out currentAge) || currentAge is < 0)
            {
                ErrorMessage = L("Fire.Error.CurrentAgeInvalid", "目前年齡格式錯誤");
                return false;
            }

            if (string.IsNullOrWhiteSpace(LifeExpectancyAge))
            {
                lifeExpectancyAge = 90;
            }
            else if (!TryParseOptionalInt(LifeExpectancyAge, out lifeExpectancyAge) || lifeExpectancyAge is <= 0)
            {
                ErrorMessage = L("Fire.Error.LifeExpectancyInvalid", "預期壽命格式錯誤");
                return false;
            }

            if (currentAge.HasValue && lifeExpectancyAge.HasValue && lifeExpectancyAge <= currentAge)
            {
                ErrorMessage = L("Fire.Error.LifeExpectancyAfterAge", "預期壽命必須大於目前年齡");
                return false;
            }

            if (!TryParseOptionalDecimal(RetirementAnnualExpenses, out retirementAnnualExpenses) || retirementAnnualExpenses is <= 0m)
            {
                ErrorMessage = L("Fire.Error.RetirementExpensesInvalid", "退休後年支出格式錯誤");
                return false;
            }

            if (!TryParseOptionalDecimal(InflationRate, out inflationRate))
            {
                ErrorMessage = L("Fire.Error.InflationInvalid", "通膨率格式錯誤");
                return false;
            }

            inflationRate ??= 0.02m;
            if (inflationRate <= -1m)
            {
                ErrorMessage = L("Fire.Error.InflationInvalid", "通膨率格式錯誤");
                return false;
            }
        }

        var now = DateTimeOffset.Now;
        scenario = new FireScenario(
            existing?.Id ?? Guid.NewGuid(),
            existing?.Name ?? "Base",
            IsAdvancedMode ? FireScenarioMode.Advanced : FireScenarioMode.Basic,
            SelectedGroup is not null ? FireNetWorthSource.PortfolioGroup : FireNetWorthSource.Manual,
            SelectedGroup?.Id,
            netWorth,
            expenses,
            savings,
            expectedReturn,
            FireReturnMode.Real,
            inflationRate,
            SavingsGrowthRate: null,
            ExpenseGrowthRate: null,
            withdrawalRate,
            CurrentAge: currentAge,
            LifeExpectancyAge: lifeExpectancyAge,
            RetirementAnnualExpenses: retirementAnnualExpenses,
            CustomTargetAmount: existing?.CustomTargetAmount,
            IncludeTaxes: existing?.IncludeTaxes ?? false,
            Notes: existing?.Notes,
            IsDefault: existing?.IsDefault ?? Scenarios.Count == 0,
            CreatedAt: existing?.CreatedAt ?? now,
            UpdatedAt: now);
        return true;
    }

    private bool TryValidateScenarioDraftName(out string name)
    {
        name = ScenarioDraftName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ScenarioDraftError = L("Fire.Scenario.NameRequired", "請輸入情境名稱");
            return false;
        }

        var candidateName = name;
        if (Scenarios.Any(s => string.Equals(s.Name, candidateName, StringComparison.CurrentCultureIgnoreCase)))
        {
            ScenarioDraftError = L("Fire.Scenario.NameDuplicate", "已有相同名稱的情境");
            return false;
        }

        ScenarioDraftError = null;
        return true;
    }

    private string GenerateUniqueScenarioName(string preferredName)
    {
        var baseName = preferredName.Trim();
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Base";

        var existingNames = Scenarios
            .Select(s => s.Name)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!existingNames.Contains(baseName))
            return baseName;

        for (var i = 2; ; i++)
        {
            var candidate = baseName + " " + i.ToString(CultureInfo.InvariantCulture);
            if (!existingNames.Contains(candidate))
                return candidate;
        }
    }

    private async Task SaveScenarioAndReloadAsync(
        FireScenario scenario,
        IReadOnlyList<FireCashFlowEvent> events)
    {
        if (_scenarioRepository is null)
            return;

        await _scenarioRepository.UpsertAsync(scenario, events).ConfigureAwait(true);
        await LoadScenariosAsync().ConfigureAwait(true);
        SelectedScenario = Scenarios.FirstOrDefault(s => s.Id == scenario.Id) ?? SelectedScenario;
    }

    private string L(string key, string fallback) =>
        _localization?.Get(key, fallback) ?? fallback;

    [RelayCommand]
    private void Calculate()
    {
        ErrorMessage = null;
        HasCalculatedResult = false;
        if (!TryParseDecimal(CurrentNetWorth, out var nw))
        { ErrorMessage = L("Fire.Error.NetWorthInvalid", "目前淨資產格式錯誤"); return; }
        if (!TryParseDecimal(AnnualExpenses, out var exp))
        { ErrorMessage = L("Fire.Error.ExpensesInvalid", "年支出格式錯誤"); return; }
        if (!TryParseDecimal(AnnualSavings, out var sav))
        { ErrorMessage = L("Fire.Error.SavingsInvalid", "年儲蓄格式錯誤"); return; }
        if (!TryParseDecimal(ExpectedAnnualReturn, out var r))
        { ErrorMessage = L("Fire.Error.ReturnInvalid", "預期報酬率格式錯誤"); return; }
        if (!TryParseDecimal(WithdrawalRate, out var w))
        { ErrorMessage = L("Fire.Error.WithdrawalInvalid", "提領率格式錯誤"); return; }

        FireProjection result;
        try
        {
            result = _calculator.Calculate(new FireInputs(nw, exp, sav, r, w));
        }
        catch (ArgumentOutOfRangeException ex)
        {
            ErrorMessage = ToFriendlyError(ex.ParamName);
            return;
        }

        FireNumber = result.FireNumber;
        YearsToFire = result.YearsToFire?.ToString() ?? "—";
        ProjectedNetWorthAtFire = result.ProjectedNetWorthAtFire;
        HasCalculatedResult = true;

        _wealthPath.Clear();
        for (int i = 0; i < result.WealthPath.Count; i++)
            _wealthPath.Add(new FireWealthPoint(i, result.WealthPath[i]));
        RefreshWealthPathChart();
    }

    private void RefreshWealthPathChart()
    {
        if (_wealthPath.Count == 0 || FireNumber <= 0m)
        {
            WealthPathChartSeries = [];
            return;
        }

        var wealthPoints = _wealthPath
            .Select(point => new ObservablePoint(point.Year, decimal.ToDouble(point.NetWorth)))
            .ToArray();
        var firstYear = _wealthPath[0].Year;
        var lastYear = _wealthPath[^1].Year;
        var fireTarget = decimal.ToDouble(FireNumber);
        var accent = new SKColor(18, 49, 88);
        var target = new SKColor(100, 116, 139);

        WealthPathChartSeries =
        [
            new LineSeries<ObservablePoint>
            {
                Name = L("Fire.Chart.WealthPath", "資產路徑"),
                Values = wealthPoints,
                Stroke = new SolidColorPaint(accent, 2f),
                Fill = new SolidColorPaint(accent.WithAlpha(28)),
                GeometrySize = 6,
                LineSmoothness = 0.35,
                AnimationsSpeed = TimeSpan.Zero,
            },
            new LineSeries<ObservablePoint>
            {
                Name = L("Fire.Chart.Target", "FIRE 目標"),
                Values =
                [
                    new ObservablePoint(firstYear, fireTarget),
                    new ObservablePoint(lastYear, fireTarget),
                ],
                Stroke = new SolidColorPaint(target, 1.5f),
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0,
                AnimationsSpeed = TimeSpan.Zero,
            },
        ];
    }

    [RelayCommand(CanExecute = nameof(HasCalculatedResult))]
    private async Task SaveToGoalsAsync()
    {
        ErrorMessage = null;
        if (!TryParseDecimal(CurrentNetWorth, out var current))
        {
            ErrorMessage = L("Fire.Error.NetWorthInvalid", "目前淨資產格式錯誤");
            return;
        }

        try
        {
            var existing = (await _goals.GetAllAsync().ConfigureAwait(true))
                .FirstOrDefault(g => string.Equals(g.Name, "FIRE", StringComparison.OrdinalIgnoreCase));
            var deadline = int.TryParse(YearsToFire, NumberStyles.Integer, CultureInfo.InvariantCulture, out var years)
                ? DateOnly.FromDateTime(DateTime.Today.AddYears(years))
                : (DateOnly?)null;
            var sourceNote = SelectedScenario is { } selectedScenario
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"Generated from FIRE scenario \"{selectedScenario.Name}\" ({selectedScenario.Id}).")
                : "Generated from FIRE calculator.";
            var goal = new FinancialGoal(
                existing?.Id ?? Guid.NewGuid(),
                "FIRE",
                FireNumber,
                current,
                deadline,
                sourceNote,
                LinkedAssetClass: null,
                // Portfolio-Groups-Refactor P6 — 把 group 連結也寫到 FIRE goal，讓 Hero
                // 在沒選 LinkedAssetClass 時走 group 算進度。
                PortfolioGroupId: SelectedGroup?.Id);

            if (existing is null)
                await _goals.AddAsync(goal).ConfigureAwait(true);
            else
                await _goals.UpdateAsync(goal).ConfigureAwait(true);

            WeakReferenceMessenger.Default.Send(new FireGoalSavedMessage(goal));
            _snackbar?.Success(L("Fire.Saved", "已同步到財務目標"));
        }
        catch (Exception ex)
        {
            _snackbar?.Error(ex.Message);
        }
    }

    private static bool TryParseDecimal(string? input, out decimal value) =>
        decimal.TryParse(input, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
        || decimal.TryParse(input, NumberStyles.Number, CultureInfo.CurrentCulture, out value);

    private static bool TryParseOptionalDecimal(string? input, out decimal? value)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            value = null;
            return true;
        }

        if (TryParseDecimal(input, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryParseOptionalInt(string? input, out int? value)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            value = null;
            return true;
        }

        if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || int.TryParse(input, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static string MonthlyAverageDisplay(string? annualValue)
    {
        if (!TryParseDecimal(annualValue, out var annual) || annual < 0m)
            return "約每月 —";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{annual:N0} ÷ 12 = 約每月 {annual / 12m:N0}");
    }

    private static string FormatMoneyInput(decimal value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);

    private static string FormatRateInput(decimal value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string ToFriendlyError(string? paramName) => paramName switch
    {
        nameof(FireInputs.CurrentNetWorth) => "目前淨資產不可為負數",
        nameof(FireInputs.AnnualExpenses) => "年支出必須大於 0",
        nameof(FireInputs.AnnualSavings) => "年儲蓄不可為負數",
        nameof(FireInputs.ExpectedAnnualReturn) => "預期報酬率必須大於 -100%",
        nameof(FireInputs.WithdrawalRate) => "安全提領率必須大於 0 且不超過 100%",
        nameof(FireInputs.MaxYears) => "模擬年數必須大於 0",
        _ => "FIRE 輸入值無效",
    };
}

public sealed record FireWealthPoint(int Year, decimal NetWorth);

public sealed class FireScenarioRowViewModel(FireScenario scenario)
{
    public FireScenario Scenario { get; } = scenario;

    public Guid Id => Scenario.Id;

    public string Name => Scenario.Name;

    public bool IsDefault => Scenario.IsDefault;

    public FireScenarioMode Mode => Scenario.Mode;

    public override string ToString() => Name;
}

public sealed class FireGoalSavedMessage(FinancialGoal goal) : ValueChangedMessage<FinancialGoal>(goal);
