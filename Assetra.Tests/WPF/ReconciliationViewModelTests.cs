using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Reconciliation;
using Assetra.WPF.Features.Import;
using Assetra.WPF.Features.Reconciliation;
using Moq;
using Xunit;

namespace Assetra.Tests.WPF;

/// <summary>
/// VM-layer tests for ReconciliationViewModel. Scope limited to synchronous
/// command branches that don't trigger LoadAsync / ReloadDiffsAsync, since
/// the latter require live data and would touch the WPF dispatcher.
/// </summary>
public sealed class ReconciliationViewModelTests
{
    private static ReconciliationViewModel CreateVm()
    {
        var service = new Mock<IReconciliationService>();
        var sessions = new Mock<IReconciliationSessionRepository>();
        var assets = new Mock<IAssetRepository>();
        var trades = new Mock<ITradeRepository>();
        var matcher = new Mock<IReconciliationMatcher>();
        return new ReconciliationViewModel(
            service.Object, sessions.Object, assets.Object, trades.Object, matcher.Object);
    }

    [Fact]
    public void ToggleNewSessionPanel_FlipsState()
    {
        var vm = CreateVm();
        Assert.False(vm.IsNewSessionPanelOpen);

        vm.ToggleNewSessionPanelCommand.Execute(null);

        Assert.True(vm.IsNewSessionPanelOpen);

        vm.ToggleNewSessionPanelCommand.Execute(null);

        Assert.False(vm.IsNewSessionPanelOpen);
    }

    [Fact]
    public void Defaults_AreSensible()
    {
        var vm = CreateVm();

        Assert.True(vm.UseExistingBatch);
        Assert.False(vm.UseUploadedFile);
        Assert.Equal(DateTime.Today, vm.NewPeriodEnd.Date);
    }

    [Fact]
    public void UseUploadedFile_True_ClearsUseExistingBatch()
    {
        var vm = CreateVm();

        vm.UseUploadedFile = true;

        Assert.False(vm.UseExistingBatch);
        Assert.True(vm.UseUploadedFile);
    }

    [Fact]
    public void UseExistingBatch_True_ClearsUseUploadedFile()
    {
        var vm = CreateVm();
        vm.UseUploadedFile = true;

        vm.UseExistingBatch = true;

        Assert.True(vm.UseExistingBatch);
        Assert.False(vm.UseUploadedFile);
    }

    [Fact]
    public async Task CreateSessionAsync_NoAccount_SetsStatus()
    {
        var vm = CreateVm();
        vm.NewSessionAccount = null;

        await vm.CreateSessionCommand.ExecuteAsync(null);

        Assert.NotNull(vm.StatusMessage);
        Assert.Contains("帳戶", vm.StatusMessage);
    }

    [Fact]
    public async Task CreateSessionAsync_PeriodEndBeforeStart_SetsStatus()
    {
        var vm = CreateVm();
        vm.NewSessionAccount = new CashAccountOption(Guid.NewGuid(), "Test");
        vm.NewPeriodStart = DateTime.Today;
        vm.NewPeriodEnd = DateTime.Today.AddDays(-7);

        await vm.CreateSessionCommand.ExecuteAsync(null);

        Assert.NotNull(vm.StatusMessage);
        Assert.Contains("結束日期", vm.StatusMessage);
    }
}
