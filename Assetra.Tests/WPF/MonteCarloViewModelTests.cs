using Assetra.Core.Interfaces.MonteCarlo;
using Assetra.Core.Models.MonteCarlo;
using Assetra.WPF.Features.MonteCarlo;
using Moq;
using Xunit;

namespace Assetra.Tests.WPF;

/// <summary>
/// VM-layer tests for MonteCarloViewModel. Covers the validation paths
/// migrated to ILocalizationService in commit b9be0d0, plus the simulator
/// success flow that sets HasResult.
/// </summary>
public sealed class MonteCarloViewModelTests
{
    private static MonteCarloViewModel CreateVm(Mock<IMonteCarloSimulator>? simulator = null)
    {
        simulator ??= new Mock<IMonteCarloSimulator>();
        return new MonteCarloViewModel(simulator.Object);
    }

    [Fact]
    public async Task Run_InvalidInitialBalance_SetsError()
    {
        var vm = CreateVm();
        vm.InitialBalance = "not-a-number";

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal("起始餘額格式錯誤", vm.ErrorMessage);
        Assert.False(vm.HasResult);
    }

    [Fact]
    public async Task Run_NegativeInitialBalance_SetsError()
    {
        var vm = CreateVm();
        vm.InitialBalance = "-1";

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal("起始餘額必須 ≥ 0", vm.ErrorMessage);
        Assert.False(vm.HasResult);
    }

    [Fact]
    public async Task Run_NegativeAnnualWithdrawal_SetsError()
    {
        var vm = CreateVm();
        vm.AnnualWithdrawal = "-1";

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal("年提領必須 ≥ 0", vm.ErrorMessage);
        Assert.False(vm.HasResult);
    }

    [Theory]
    [InlineData("-1.5", "平均報酬率必須 > -100%")]   // mu <= -1
    [InlineData("-2", "平均報酬率必須 > -100%")]
    public async Task Run_InvalidMeanReturn_SetsError(string meanReturn, string expected)
    {
        var vm = CreateVm();
        vm.MeanReturn = meanReturn;

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(expected, vm.ErrorMessage);
    }

    [Fact]
    public async Task Run_NegativeStdDev_SetsError()
    {
        var vm = CreateVm();
        vm.StdDev = "-0.1";

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal("標準差必須 ≥ 0", vm.ErrorMessage);
    }

    [Fact]
    public async Task Run_YearsExceedingMax_SetsBoundedError()
    {
        var vm = CreateVm();
        vm.Years = (MonteCarloInputs.MaxYears + 1).ToString();

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Contains(MonteCarloInputs.MaxYears.ToString(), vm.ErrorMessage);
    }

    [Fact]
    public async Task Run_ZeroSimulationCount_SetsError()
    {
        var vm = CreateVm();
        vm.SimulationCount = "0";

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal("模擬次數必須 > 0", vm.ErrorMessage);
    }

    [Fact]
    public async Task Run_Success_PopulatesResultAndPath()
    {
        var simulator = new Mock<IMonteCarloSimulator>();
        var path = new decimal[] { 10_000_000m, 9_500_000m, 9_100_000m };
        simulator.Setup(s => s.Simulate(It.IsAny<MonteCarloInputs>()))
                 .Returns(new MonteCarloResult(0.85m, 8_500_000m, 5_000_000m, 12_000_000m, path));
        var vm = CreateVm(simulator);

        await vm.RunCommand.ExecuteAsync(null);

        Assert.True(vm.HasResult);
        Assert.Null(vm.ErrorMessage);
        Assert.Equal(0.85m, vm.SuccessRate);
        Assert.Equal(8_500_000m, vm.MedianEnding);
        Assert.Equal(5_000_000m, vm.P10Ending);
        Assert.Equal(12_000_000m, vm.P90Ending);
        Assert.Null(vm.MedianDepletionYear);
        Assert.Equal("未耗盡", vm.MedianDepletionYearDisplay);
        Assert.Equal(3, vm.MedianPath.Count);
        Assert.Equal(0, vm.MedianPath[0].Year);
        Assert.Equal(10_000_000m, vm.MedianPath[0].Balance);
    }

    [Fact]
    public async Task Run_Success_PopulatesMedianPathTilesWithChangeAndTone()
    {
        var simulator = new Mock<IMonteCarloSimulator>();
        var path = new decimal[] { 10_000_000m, 9_500_000m, 9_600_000m, 0m };
        simulator.Setup(s => s.Simulate(It.IsAny<MonteCarloInputs>()))
                 .Returns(new MonteCarloResult(0.35m, 0m, 0m, 12_000_000m, path));
        var vm = CreateVm(simulator);

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(4, vm.MedianPathTiles.Count);
        Assert.Equal(0, vm.MedianPathTiles[0].Year);
        Assert.Equal(10_000_000m, vm.MedianPathTiles[0].Balance);
        Assert.Null(vm.MedianPathTiles[0].Change);
        Assert.Equal(MonteCarloPathTone.Neutral, vm.MedianPathTiles[0].Tone);
        Assert.Equal(-500_000m, vm.MedianPathTiles[1].Change);
        Assert.Equal(MonteCarloPathTone.Negative, vm.MedianPathTiles[1].Tone);
        Assert.Equal(100_000m, vm.MedianPathTiles[2].Change);
        Assert.Equal(MonteCarloPathTone.Positive, vm.MedianPathTiles[2].Tone);
        Assert.Equal(MonteCarloPathTone.Depleted, vm.MedianPathTiles[3].Tone);
    }

    [Fact]
    public async Task Run_WithDepletedPaths_PopulatesDepletionYearDisplay()
    {
        var simulator = new Mock<IMonteCarloSimulator>();
        simulator.Setup(s => s.Simulate(It.IsAny<MonteCarloInputs>()))
                 .Returns(new MonteCarloResult(
                     SuccessRate: 0.35m,
                     MedianEndingBalance: 0m,
                     P10EndingBalance: 0m,
                     P90EndingBalance: 1_000_000m,
                     MedianBalancePath: Array.Empty<decimal>(),
                     MedianDepletionYear: 12));
        var vm = CreateVm(simulator);

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(12, vm.MedianDepletionYear);
        Assert.Equal("中位第 12 年", vm.MedianDepletionYearDisplay);
    }

    [Fact]
    public async Task Run_SimulatorThrows_SetsFriendlyError()
    {
        var simulator = new Mock<IMonteCarloSimulator>();
        simulator.Setup(s => s.Simulate(It.IsAny<MonteCarloInputs>()))
                 .Throws(new ArgumentOutOfRangeException("Years", "Invalid range."));
        var vm = CreateVm(simulator);

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal("模擬失敗，請檢查輸入參數後再試一次", vm.ErrorMessage);
        Assert.False(vm.HasResult);
        Assert.False(vm.IsRunning);
    }

    [Fact]
    public async Task Run_SimulatorThrows_ClearsMedianPathTiles()
    {
        var simulator = new Mock<IMonteCarloSimulator>();
        simulator.SetupSequence(s => s.Simulate(It.IsAny<MonteCarloInputs>()))
                 .Returns(new MonteCarloResult(
                     0.85m,
                     8_500_000m,
                     5_000_000m,
                     12_000_000m,
                     new decimal[] { 10_000_000m, 9_500_000m }))
                 .Throws(new ArgumentOutOfRangeException("Years", "Invalid range."));
        var vm = CreateVm(simulator);

        await vm.RunCommand.ExecuteAsync(null);
        await vm.RunCommand.ExecuteAsync(null);

        Assert.False(vm.HasResult);
        Assert.Empty(vm.MedianPathTiles);
    }

    [Fact]
    public async Task Run_WhileAlreadyRunning_DoesNothing()
    {
        var vm = CreateVm();
        // Force IsRunning so the early-return path triggers.
        var prop = typeof(MonteCarloViewModel).GetProperty(nameof(MonteCarloViewModel.IsRunning))!;
        prop.SetValue(vm, true);

        await vm.RunCommand.ExecuteAsync(null);

        // No simulator setup means a Simulate call would throw; passing means we returned early.
        Assert.False(vm.HasResult);
    }
}
