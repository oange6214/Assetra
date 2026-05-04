using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Fire;
using Assetra.Core.Models;
using Assetra.Core.Models.Fire;
using Assetra.WPF.Features.Fire;
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
        Mock<IFinancialGoalRepository>? goals = null)
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
        return new FireViewModel(calculator.Object, goals.Object);
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
}
