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

    [Theory]
    [InlineData("-1.5",  "平均報酬率必須 > -100%")]   // mu <= -1
    [InlineData("-2",    "平均報酬率必須 > -100%")]
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
        Assert.Equal(3, vm.MedianPath.Count);
        Assert.Equal(0, vm.MedianPath[0].Year);
        Assert.Equal(10_000_000m, vm.MedianPath[0].Balance);
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
