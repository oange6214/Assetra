using System.Globalization;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Fire;
using Assetra.Core.Models;
using Assetra.Core.Models.Fire;
using Assetra.Core.Models.MonteCarlo;
using Assetra.WPF.Features.Fire;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using Moq;
using Xunit;

namespace Assetra.Tests.WPF;

/// <summary>
/// VM-layer tests for FireViewModel. Covers the validation paths that were
/// migrated from hard-coded zh-TW strings to ILocalizationService in commit
/// b9be0d0, plus the calculator success and goal-sync flows.
/// </summary>
public sealed class FireViewModelTests
{
    private static FireViewModel CreateVm(
        Mock<IFireCalculatorService>? calculator = null,
        Mock<IFinancialGoalRepository>? goals = null,
        Mock<IFireScenarioRepository>? scenarios = null,
        IAppNetWorthProvider? appNetWorthProvider = null,
        Mock<IFirePlanningService>? planning = null,
        Mock<IFireDrawdownService>? drawdown = null,
        Mock<IFireMonteCarloService>? monteCarlo = null)
    {
        calculator ??= new Mock<IFireCalculatorService>();
        if (goals is null)
        {
            // Default mock returns no goals — for tests that don't care about
            // the goals path. When the caller supplies a goals mock with its
            // own setup, leave it alone.
            goals = new Mock<IFinancialGoalRepository>();
            goals.Setup(g => g.GetAllAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Array.Empty<FinancialGoal>());
        }
        return new FireViewModel(
            calculator.Object,
            goals.Object,
            scenarioRepository: scenarios?.Object,
            appNetWorthProvider: appNetWorthProvider,
            planningService: planning?.Object,
            drawdownService: drawdown?.Object,
            monteCarloService: monteCarlo?.Object);
    }

    [Theory]
    [InlineData("not-a-number", "目前淨資產格式錯誤")]
    [InlineData("", "目前淨資產格式錯誤")]
    public void Calculate_InvalidNetWorth_SetsErrorMessage(string input, string expectedFallback)
    {
        var vm = CreateVm();
        vm.CurrentNetWorth = input;

        vm.CalculateCommand.Execute(null);

        Assert.Equal(expectedFallback, vm.ErrorMessage);
        Assert.False(vm.HasCalculatedResult);
    }

    [Fact]
    public void Calculate_InvalidExpenses_SetsErrorMessage()
    {
        var vm = CreateVm();
        vm.AnnualExpenses = "abc";

        vm.CalculateCommand.Execute(null);

        Assert.Equal("年支出格式錯誤", vm.ErrorMessage);
    }

    [Fact]
    public void AnnualExpensesMonthlyAverageDisplay_DividesAnnualAmountByTwelve()
    {
        var vm = CreateVm();

        vm.AnnualExpenses = "600,000";

        Assert.Equal("600,000 ÷ 12 = 約每月 50,000", vm.AnnualExpensesMonthlyAverageDisplay);
    }

    [Fact]
    public void AnnualSavingsMonthlyAverageDisplay_DividesAnnualAmountByTwelve()
    {
        var vm = CreateVm();

        vm.AnnualSavings = "300,000";

        Assert.Equal("300,000 ÷ 12 = 約每月 25,000", vm.AnnualSavingsMonthlyAverageDisplay);
    }

    [Fact]
    public void MonthlyAverageDisplay_InvalidInput_ShowsPlaceholder()
    {
        var vm = CreateVm();

        vm.AnnualExpenses = "abc";
        vm.AnnualSavings = "";

        Assert.Equal("約每月 —", vm.AnnualExpensesMonthlyAverageDisplay);
        Assert.Equal("約每月 —", vm.AnnualSavingsMonthlyAverageDisplay);
    }

    [Fact]
    public void InflationRateHintDisplay_TranslatesDecimalRateToAnnualPercent()
    {
        var vm = CreateVm();

        vm.InflationRate = "0.025";

        Assert.Equal("0.025 = 每年 2.5%", vm.InflationRateHintDisplay);
    }

    [Fact]
    public void IsBasicMode_TogglesAdvancedModeForSegmentedControlBinding()
    {
        var vm = CreateVm();

        vm.IsAdvancedMode = true;
        vm.IsBasicMode = true;

        Assert.False(vm.IsAdvancedMode);
        Assert.True(vm.IsBasicMode);
    }

    [Fact]
    public void FireFormulaDisplay_ExplainsRequiredAssetsFormula()
    {
        var vm = CreateVm();

        vm.AnnualExpenses = "600,000";
        vm.WithdrawalRate = "0.04";

        Assert.Equal("600,000 ÷ 4.00% = 15,000,000", vm.FireFormulaDisplay);
    }

    [Fact]
    public void PlanningResultDerivedState_ExposesWarningsDrawdownAndMonthlyGap()
    {
        var vm = CreateVm();

        vm.PlanningResult = new FirePlanningProjection(
            RequiredAssets: 15_000_000m,
            YearsToFire: 12,
            FireYear: 2038,
            ProjectedNetWorthAtFire: 15_500_000m,
            RequiredMonthlySavings: 12_345m,
            MonteCarloSuccessRate: null,
            AccumulationPath: Array.Empty<Assetra.Core.Models.Fire.FireWealthPoint>(),
            DrawdownPath: new[]
            {
                new FireDrawdownPoint(
                    Year: 2038,
                    Age: 45,
                    StartingBalance: 15_500_000m,
                    InvestmentReturn: 620_000m,
                    AnnualWithdrawal: 600_000m,
                    NetCashFlow: -600_000m,
                    EndingBalance: 15_520_000m),
            },
            Warnings: new[]
            {
                new FireProjectionWarning(
                    FireProjectionWarningCode.WithdrawalRateAboveCommonRange,
                    "安全提領率偏高"),
            });

        Assert.True(vm.HasPlanningWarnings);
        Assert.True(vm.HasDrawdownPath);
        Assert.Equal("12,345", vm.RequiredMonthlySavingsDisplay);
    }

    [Fact]
    public async Task CalculatePlanningAsync_AdvancedScenarioWithRetirementHorizon_AddsMonteCarloSuccessRate()
    {
        var scenario = CreateScenario(
            "Base",
            isDefault: true,
            mode: FireScenarioMode.Advanced,
            currentAge: 45,
            lifeExpectancyAge: 90);
        var scenarios = new Mock<IFireScenarioRepository>();
        scenarios.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { scenario });
        scenarios.Setup(r => r.GetCashFlowEventsAsync(scenario.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FireCashFlowEvent>());
        var planning = new Mock<IFirePlanningService>();
        planning.Setup(p => p.Project(It.IsAny<FireScenario>(), It.IsAny<IReadOnlyList<FireCashFlowEvent>>(), 2026, It.IsAny<int>()))
            .Returns(new FirePlanningProjection(
                RequiredAssets: 15_000_000m,
                YearsToFire: 5,
                FireYear: 2031,
                ProjectedNetWorthAtFire: 15_500_000m,
                RequiredMonthlySavings: 0m,
                MonteCarloSuccessRate: null,
                AccumulationPath: new[] { new Assetra.Core.Models.Fire.FireWealthPoint(0, 1_000_000m) },
                DrawdownPath: Array.Empty<FireDrawdownPoint>(),
                Warnings: Array.Empty<FireProjectionWarning>()));
        var monteCarlo = new Mock<IFireMonteCarloService>();
        monteCarlo.Setup(m => m.EstimateRetirementSuccess(
                It.IsAny<FireScenario>(),
                15_500_000m,
                40,
                It.IsAny<int>(),
                It.IsAny<int?>(),
                It.IsAny<decimal>()))
            .Returns(new MonteCarloResult(
                SuccessRate: 0.88m,
                MedianEndingBalance: 20_000_000m,
                P10EndingBalance: 5_000_000m,
                P90EndingBalance: 40_000_000m,
                MedianBalancePath: Array.Empty<decimal>()));
        var vm = CreateVm(scenarios: scenarios, planning: planning, monteCarlo: monteCarlo);

        await vm.LoadScenariosAsync();
        await vm.CalculatePlanningCommand.ExecuteAsync(null);

        Assert.Equal(0.88m, vm.PlanningResult?.MonteCarloSuccessRate);
        monteCarlo.Verify();
    }

    [Fact]
    public async Task SelectedScenarioChanged_AfterPlanningResult_RecalculatesForNewScenario()
    {
        var baseScenario = CreateScenario(
            "Base",
            isDefault: true,
            currentNetWorth: 1_000_000m,
            annualSavings: 300_000m);
        var fasterScenario = CreateScenario(
            "Faster",
            isDefault: false,
            currentNetWorth: 2_000_000m,
            annualSavings: 800_000m);
        var scenarios = new Mock<IFireScenarioRepository>();
        scenarios.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { baseScenario, fasterScenario });
        scenarios.Setup(r => r.GetCashFlowEventsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FireCashFlowEvent>());
        var planning = new Mock<IFirePlanningService>();
        planning.Setup(p => p.Project(
                It.IsAny<FireScenario>(),
                It.IsAny<IReadOnlyList<FireCashFlowEvent>>(),
                It.IsAny<int>(),
                It.IsAny<int>()))
            .Returns((FireScenario scenario, IReadOnlyList<FireCashFlowEvent> _, int _, int _) =>
                scenario.Name == "Faster"
                    ? new FirePlanningProjection(
                        RequiredAssets: 20_000_000m,
                        YearsToFire: 6,
                        FireYear: 2032,
                        ProjectedNetWorthAtFire: 20_500_000m,
                        RequiredMonthlySavings: 0m,
                        MonteCarloSuccessRate: null,
                        AccumulationPath: new[] { new Assetra.Core.Models.Fire.FireWealthPoint(0, 2_000_000m) },
                        DrawdownPath: Array.Empty<FireDrawdownPoint>(),
                        Warnings: Array.Empty<FireProjectionWarning>())
                    : new FirePlanningProjection(
                        RequiredAssets: 15_000_000m,
                        YearsToFire: 12,
                        FireYear: 2038,
                        ProjectedNetWorthAtFire: 15_500_000m,
                        RequiredMonthlySavings: 0m,
                        MonteCarloSuccessRate: null,
                        AccumulationPath: new[] { new Assetra.Core.Models.Fire.FireWealthPoint(0, 1_000_000m) },
                        DrawdownPath: Array.Empty<FireDrawdownPoint>(),
                        Warnings: Array.Empty<FireProjectionWarning>()));
        var vm = CreateVm(scenarios: scenarios, planning: planning);

        await vm.LoadScenariosAsync();
        await vm.CalculatePlanningCommand.ExecuteAsync(null);
        vm.SelectedScenario = vm.Scenarios.Single(s => s.Name == "Faster");
        if (vm.CalculatePlanningCommand.ExecutionTask is { } recalculation)
            await recalculation;

        Assert.Equal("6", vm.YearsToFire);
        Assert.Equal(20_000_000m, vm.FireNumber);
        Assert.Equal(2_000_000m, vm.WealthPath.Single().NetWorth);
        planning.Verify(p => p.Project(
            It.IsAny<FireScenario>(),
            It.IsAny<IReadOnlyList<FireCashFlowEvent>>(),
            It.IsAny<int>(),
            It.IsAny<int>()), Times.Exactly(2));
    }

    [Fact]
    public async Task LoadScenariosAsync_LoadsDefaultScenarioAndAppliesInputs()
    {
        var scenario = CreateScenario(
            "Base",
            isDefault: true,
            mode: FireScenarioMode.Advanced,
            currentNetWorth: 7_271_042m,
            annualExpenses: 720_000m,
            annualSavings: 360_000m,
            expectedAnnualReturn: 0.06m,
            withdrawalRate: 0.035m,
            currentAge: 42,
            lifeExpectancyAge: 92,
            retirementAnnualExpenses: 660_000m,
            inflationRate: 0.025m);
        var scenarios = new Mock<IFireScenarioRepository>();
        scenarios.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { scenario });

        var vm = CreateVm(scenarios: scenarios);

        await vm.LoadScenariosAsync();

        Assert.Single(vm.Scenarios);
        Assert.Equal("Base", vm.SelectedScenario?.Name);
        Assert.Equal("Base", vm.SelectedScenario?.ToString());
        Assert.True(vm.IsAdvancedMode);
        Assert.Equal("7,271,042", vm.CurrentNetWorth);
        Assert.Equal("720,000", vm.AnnualExpenses);
        Assert.Equal("360,000", vm.AnnualSavings);
        Assert.Equal("0.06", vm.ExpectedAnnualReturn);
        Assert.Equal("0.035", vm.WithdrawalRate);
        Assert.Equal("42", vm.CurrentAge);
        Assert.Equal("92", vm.LifeExpectancyAge);
        Assert.Equal("660,000", vm.RetirementAnnualExpenses);
        Assert.Equal("0.025", vm.InflationRate);
    }

    [Fact]
    public async Task SaveScenarioAsync_AdvancedInputs_PersistScenarioAssumptions()
    {
        var scenarios = new Mock<IFireScenarioRepository>();
        scenarios.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FireScenario>());
        FireScenario? saved = null;
        scenarios.Setup(r => r.UpsertAsync(
                It.IsAny<FireScenario>(),
                It.IsAny<IReadOnlyList<FireCashFlowEvent>>(),
                It.IsAny<CancellationToken>()))
            .Callback<FireScenario, IReadOnlyList<FireCashFlowEvent>, CancellationToken>((s, _, _) => saved = s)
            .Returns(Task.CompletedTask);

        var vm = CreateVm(scenarios: scenarios);
        vm.IsAdvancedMode = true;
        vm.CurrentNetWorth = "8,499,990";
        vm.AnnualExpenses = "600,000";
        vm.AnnualSavings = "800,000";
        vm.ExpectedAnnualReturn = "0.10";
        vm.WithdrawalRate = "0.04";
        vm.CurrentAge = "41";
        vm.LifeExpectancyAge = "95";
        vm.RetirementAnnualExpenses = "720,000";
        vm.InflationRate = "0.025";

        await vm.SaveScenarioCommand.ExecuteAsync(null);

        Assert.NotNull(saved);
        Assert.Equal(FireScenarioMode.Advanced, saved.Mode);
        Assert.Equal(41, saved.CurrentAge);
        Assert.Equal(95, saved.LifeExpectancyAge);
        Assert.Equal(720_000m, saved.RetirementAnnualExpenses);
        Assert.Equal(0.025m, saved.InflationRate);
    }

    [Fact]
    public async Task CreateScenarioCommand_OpensNameDialogWithUniqueDraftName()
    {
        var scenario = CreateScenario("Base", isDefault: true);
        var scenarios = new Mock<IFireScenarioRepository>();
        scenarios.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { scenario });
        var vm = CreateVm(scenarios: scenarios);

        await vm.LoadScenariosAsync();
        await vm.CreateScenarioCommand.ExecuteAsync(null);

        Assert.True(vm.IsCreateScenarioOpen);
        Assert.False(string.IsNullOrWhiteSpace(vm.ScenarioDraftName));
        Assert.NotEqual("Base", vm.ScenarioDraftName);
        Assert.Null(vm.ScenarioDraftError);
    }

    [Fact]
    public async Task ConfirmCreateScenarioCommand_DuplicateNameKeepsDialogAndDoesNotPersist()
    {
        var scenario = CreateScenario("Base", isDefault: true);
        var scenarios = new Mock<IFireScenarioRepository>();
        scenarios.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { scenario });
        var vm = CreateVm(scenarios: scenarios);

        await vm.LoadScenariosAsync();
        await vm.CreateScenarioCommand.ExecuteAsync(null);
        vm.ScenarioDraftName = "Base";
        await vm.ConfirmCreateScenarioCommand.ExecuteAsync(null);

        Assert.True(vm.IsCreateScenarioOpen);
        Assert.NotNull(vm.ScenarioDraftError);
        scenarios.Verify(r => r.UpsertAsync(
            It.IsAny<FireScenario>(),
            It.IsAny<IReadOnlyList<FireCashFlowEvent>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmCreateScenarioCommand_UniqueNamePersistsProvidedName()
    {
        var scenario = CreateScenario("Base", isDefault: true);
        var scenarios = new Mock<IFireScenarioRepository>();
        scenarios.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { scenario });
        FireScenario? saved = null;
        scenarios.Setup(r => r.UpsertAsync(
                It.IsAny<FireScenario>(),
                It.IsAny<IReadOnlyList<FireCashFlowEvent>>(),
                It.IsAny<CancellationToken>()))
            .Callback<FireScenario, IReadOnlyList<FireCashFlowEvent>, CancellationToken>((s, _, _) => saved = s)
            .Returns(Task.CompletedTask);
        var vm = CreateVm(scenarios: scenarios);

        await vm.LoadScenariosAsync();
        await vm.CreateScenarioCommand.ExecuteAsync(null);
        vm.ScenarioDraftName = "退休提前";
        await vm.ConfirmCreateScenarioCommand.ExecuteAsync(null);

        Assert.NotNull(saved);
        Assert.Equal("退休提前", saved.Name);
        Assert.False(saved.IsDefault);
        Assert.False(vm.IsCreateScenarioOpen);
    }

    [Fact]
    public async Task DuplicateScenarioCommand_UsesUniqueCopyName()
    {
        var scenario = CreateScenario("Base", isDefault: true);
        var existingCopy = CreateScenario("Base Copy", isDefault: false);
        var scenarios = new Mock<IFireScenarioRepository>();
        scenarios.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { scenario, existingCopy });
        scenarios.Setup(r => r.GetCashFlowEventsAsync(scenario.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FireCashFlowEvent>());
        FireScenario? saved = null;
        scenarios.Setup(r => r.UpsertAsync(
                It.IsAny<FireScenario>(),
                It.IsAny<IReadOnlyList<FireCashFlowEvent>>(),
                It.IsAny<CancellationToken>()))
            .Callback<FireScenario, IReadOnlyList<FireCashFlowEvent>, CancellationToken>((s, _, _) => saved = s)
            .Returns(Task.CompletedTask);
        var vm = CreateVm(scenarios: scenarios);

        await vm.LoadScenariosAsync();
        await vm.DuplicateScenarioCommand.ExecuteAsync(null);

        Assert.NotNull(saved);
        Assert.Equal("Base Copy 2", saved.Name);
        Assert.False(saved.IsDefault);
    }

    [Fact]
    public async Task IsAdvancedMode_DoesNotClearSelectedScenario()
    {
        var scenario = CreateScenario("Base", isDefault: true);
        var scenarios = new Mock<IFireScenarioRepository>();
        scenarios.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { scenario });
        var vm = CreateVm(scenarios: scenarios);

        await vm.LoadScenariosAsync();
        vm.IsAdvancedMode = true;
        vm.IsAdvancedMode = false;

        Assert.Equal(scenario.Id, vm.SelectedScenario?.Id);
        Assert.Single(vm.Scenarios);
    }

    [Fact]
    public void Calculate_ServiceArgumentOutOfRange_TranslatesToFriendlyMessage()
    {
        var calc = new Mock<IFireCalculatorService>();
        calc.Setup(c => c.Calculate(It.IsAny<FireInputs>()))
            .Throws(new ArgumentOutOfRangeException(nameof(FireInputs.WithdrawalRate)));
        var vm = CreateVm(calc);

        vm.CalculateCommand.Execute(null);

        Assert.Equal("安全提領率必須大於 0 且不超過 100%", vm.ErrorMessage);
        Assert.False(vm.HasCalculatedResult);
    }

    [Fact]
    public void Calculate_Success_PopulatesResultAndWealthPath()
    {
        var calc = new Mock<IFireCalculatorService>();
        var path = new decimal[] { 1_000_000m, 1_100_000m, 1_250_000m };
        calc.Setup(c => c.Calculate(It.IsAny<FireInputs>()))
            .Returns(new FireProjection(15_000_000m, 18, 15_500_000m, path));
        var vm = CreateVm(calc);

        vm.CalculateCommand.Execute(null);

        Assert.True(vm.HasCalculatedResult);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(15_000_000m, vm.FireNumber);
        Assert.Equal("18", vm.YearsToFire);
        Assert.Equal(15_500_000m, vm.ProjectedNetWorthAtFire);
        Assert.Equal(3, vm.WealthPath.Count);
        Assert.Equal(0, vm.WealthPath[0].Year);
        Assert.Equal(1_000_000m, vm.WealthPath[0].NetWorth);
    }

    [Fact]
    public void Calculate_Success_BuildsWealthPathChartWithFireTargetReference()
    {
        var calc = new Mock<IFireCalculatorService>();
        var path = new decimal[] { 1_000_000m, 1_100_000m, 1_250_000m };
        calc.Setup(c => c.Calculate(It.IsAny<FireInputs>()))
            .Returns(new FireProjection(15_000_000m, 18, 15_500_000m, path));
        var vm = CreateVm(calc);

        vm.CalculateCommand.Execute(null);

        Assert.True(vm.HasWealthPathChart);
        var wealthSeries = Assert.IsType<LineSeries<ObservablePoint>>(vm.WealthPathChartSeries[0]);
        var wealthPoints = Assert.IsAssignableFrom<IEnumerable<ObservablePoint>>(wealthSeries.Values!).ToArray();
        Assert.Equal(3, wealthPoints.Length);
        Assert.Equal(0d, wealthPoints[0].X);
        Assert.Equal(1_000_000d, wealthPoints[0].Y);

        var targetSeries = Assert.IsType<LineSeries<ObservablePoint>>(vm.WealthPathChartSeries[1]);
        var targetPoints = Assert.IsAssignableFrom<IEnumerable<ObservablePoint>>(targetSeries.Values!).ToArray();
        Assert.Equal(2, targetPoints.Length);
        Assert.All(targetPoints, point => Assert.Equal(15_000_000d, point.Y));
    }

    [Fact]
    public async Task LoadCurrentNetWorthAsync_UsesApplicationNetWorthBeforeUserEdits()
    {
        var provider = new Mock<IAppNetWorthProvider>();
        provider.Setup(p => p.GetCurrentNetWorthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(7_271_042m);
        var vm = CreateVm(appNetWorthProvider: provider.Object);

        await vm.LoadCurrentNetWorthAsync();

        Assert.Equal("7,271,042", vm.CurrentNetWorth);
    }

    [Fact]
    public async Task LoadCurrentNetWorthAsync_DoesNotOverwriteUserEnteredNetWorth()
    {
        var provider = new Mock<IAppNetWorthProvider>();
        provider.Setup(p => p.GetCurrentNetWorthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(7_271_042m);
        var vm = CreateVm(appNetWorthProvider: provider.Object);

        vm.CurrentNetWorth = "123,456";
        await vm.LoadCurrentNetWorthAsync();

        Assert.Equal("123,456", vm.CurrentNetWorth);
    }

    [Fact]
    public async Task LoadCurrentNetWorthAsync_DoesNotOverwriteLoadedScenarioNetWorth()
    {
        var scenario = CreateScenario(
            "Base",
            isDefault: true,
            currentNetWorth: 8_499_990m);
        var scenarios = new Mock<IFireScenarioRepository>();
        scenarios.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { scenario });
        var provider = new Mock<IAppNetWorthProvider>();
        provider.Setup(p => p.GetCurrentNetWorthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(7_271_042m);
        var vm = CreateVm(scenarios: scenarios, appNetWorthProvider: provider.Object);

        await vm.LoadScenariosAsync();
        await vm.LoadCurrentNetWorthAsync();

        Assert.Equal("8,499,990", vm.CurrentNetWorth);
    }

    [Fact]
    public void Calculate_NoSolution_RendersDashForYearsToFire()
    {
        var calc = new Mock<IFireCalculatorService>();
        calc.Setup(c => c.Calculate(It.IsAny<FireInputs>()))
            .Returns(new FireProjection(15_000_000m, null, 5_000_000m, new[] { 1_000_000m, 950_000m }));
        var vm = CreateVm(calc);

        vm.CalculateCommand.Execute(null);

        Assert.True(vm.HasCalculatedResult);
        Assert.Equal("—", vm.YearsToFire);
    }

    [Fact]
    public void SaveToGoalsCommand_DisabledBeforeCalculate()
    {
        var vm = CreateVm();

        Assert.False(vm.SaveToGoalsCommand.CanExecute(null));
    }

    [Fact]
    public async Task SaveToGoalsAsync_NoExistingFireGoal_AddsNewGoal()
    {
        var calc = new Mock<IFireCalculatorService>();
        calc.Setup(c => c.Calculate(It.IsAny<FireInputs>()))
            .Returns(new FireProjection(15_000_000m, 12, 15_300_000m, new[] { 1_000_000m }));
        var goals = new Mock<IFinancialGoalRepository>();
        goals.Setup(g => g.GetAllAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(Array.Empty<FinancialGoal>());

        var vm = CreateVm(calc, goals);
        vm.CalculateCommand.Execute(null);

        await vm.SaveToGoalsCommand.ExecuteAsync(null);

        goals.Verify(g => g.AddAsync(
            It.Is<FinancialGoal>(fg => fg.Name == "FIRE" && fg.TargetAmount == 15_000_000m),
            It.IsAny<CancellationToken>()), Times.Once);
        goals.Verify(g => g.UpdateAsync(It.IsAny<FinancialGoal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveToGoalsAsync_ExistingFireGoal_UpdatesInsteadOfAdding()
    {
        var existing = new FinancialGoal(Guid.NewGuid(), "FIRE", 10_000_000m, 500_000m, null, null);
        var calc = new Mock<IFireCalculatorService>();
        calc.Setup(c => c.Calculate(It.IsAny<FireInputs>()))
            .Returns(new FireProjection(15_000_000m, 12, 15_300_000m, new[] { 1_000_000m }));
        var goals = new Mock<IFinancialGoalRepository>();
        goals.Setup(g => g.GetAllAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(new[] { existing });

        var vm = CreateVm(calc, goals);
        vm.CalculateCommand.Execute(null);

        await vm.SaveToGoalsCommand.ExecuteAsync(null);

        goals.Verify(g => g.UpdateAsync(
            It.Is<FinancialGoal>(fg => fg.Id == existing.Id && fg.TargetAmount == 15_000_000m),
            It.IsAny<CancellationToken>()), Times.Once);
        goals.Verify(g => g.AddAsync(It.IsAny<FinancialGoal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveToGoalsAsync_SelectedScenario_AddsScenarioSourceToNotes()
    {
        var scenario = CreateScenario("Base", isDefault: true, mode: FireScenarioMode.Advanced);
        var scenarios = new Mock<IFireScenarioRepository>();
        scenarios.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { scenario });
        scenarios.Setup(r => r.GetCashFlowEventsAsync(scenario.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<FireCashFlowEvent>());
        var planning = new Mock<IFirePlanningService>();
        planning.Setup(p => p.Project(It.IsAny<FireScenario>(), It.IsAny<IReadOnlyList<FireCashFlowEvent>>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new FirePlanningProjection(
                RequiredAssets: 15_000_000m,
                YearsToFire: 12,
                FireYear: 2038,
                ProjectedNetWorthAtFire: 15_300_000m,
                RequiredMonthlySavings: 0m,
                MonteCarloSuccessRate: null,
                AccumulationPath: new[] { new Assetra.Core.Models.Fire.FireWealthPoint(0, 1_000_000m) },
                DrawdownPath: Array.Empty<FireDrawdownPoint>(),
                Warnings: Array.Empty<FireProjectionWarning>()));
        var goals = new Mock<IFinancialGoalRepository>();
        goals.Setup(g => g.GetAllAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(Array.Empty<FinancialGoal>());
        var vm = CreateVm(goals: goals, scenarios: scenarios, planning: planning);

        await vm.LoadScenariosAsync();
        await vm.CalculatePlanningCommand.ExecuteAsync(null);
        await vm.SaveToGoalsCommand.ExecuteAsync(null);

        goals.Verify(g => g.AddAsync(
            It.Is<FinancialGoal>(fg => fg.Notes != null && fg.Notes.Contains("Base", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static FireScenario CreateScenario(
        string name,
        bool isDefault,
        FireScenarioMode mode = FireScenarioMode.Basic,
        decimal currentNetWorth = 1_000_000m,
        decimal annualExpenses = 600_000m,
        decimal annualSavings = 300_000m,
        decimal expectedAnnualReturn = 0.05m,
        decimal withdrawalRate = 0.04m,
        int? currentAge = null,
        int? lifeExpectancyAge = null,
        decimal? retirementAnnualExpenses = null,
        decimal? inflationRate = null)
    {
        var now = DateTimeOffset.Parse("2026-05-29T00:00:00+08:00", CultureInfo.InvariantCulture);
        return new FireScenario(
            Guid.NewGuid(),
            name,
            mode,
            FireNetWorthSource.Manual,
            PortfolioGroupId: null,
            CurrentNetWorthOverride: currentNetWorth,
            AnnualExpenses: annualExpenses,
            AnnualSavings: annualSavings,
            ExpectedAnnualReturn: expectedAnnualReturn,
            ReturnMode: FireReturnMode.Real,
            InflationRate: inflationRate ?? (mode == FireScenarioMode.Advanced ? 0.02m : null),
            SavingsGrowthRate: null,
            ExpenseGrowthRate: null,
            WithdrawalRate: withdrawalRate,
            CurrentAge: currentAge,
            LifeExpectancyAge: lifeExpectancyAge ?? (mode == FireScenarioMode.Advanced ? 90 : null),
            RetirementAnnualExpenses: retirementAnnualExpenses,
            CustomTargetAmount: null,
            IncludeTaxes: false,
            Notes: null,
            IsDefault: isDefault,
            CreatedAt: now,
            UpdatedAt: now);
    }
}
