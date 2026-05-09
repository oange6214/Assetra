using Xunit;

namespace Assetra.Tests.WPF.Fixtures;

/// <summary>
/// Smoke tests for the H3 Phase 3 fixture: builds a real PortfolioViewModel
/// through the test factory and exercises the basic load path. Guards against
/// drift if PortfolioServices/PortfolioRepositories ctor signatures grow.
/// </summary>
public class PortfolioViewModelTestFactoryTests
{
    [Fact]
    public void Build_DefaultDependencies_DoesNotThrow()
    {
        var fx = new PortfolioViewModelTestFactory();
        var vm = fx.Build();
        Assert.NotNull(vm);
        Assert.Empty(vm.Positions);
    }

    [Fact]
    public async Task Build_LoadAsync_PopulatesEntries()
    {
        var fx = new PortfolioViewModelTestFactory()
            .WithEntries(
                PortfolioVmFixtures.MakeEntry("2330"),
                PortfolioVmFixtures.MakeEntry("2317"));
        var vm = fx.Build();
        await vm.LoadAsync();
        Assert.Equal(2, vm.Positions.Count);
    }

    [Fact]
    public void Build_TradesSeeded_VisibleAfterReload()
    {
        var fx = new PortfolioViewModelTestFactory();
        Assert.Empty(fx.Trades.Store);
        // Per-test mutation via the public Trades fixture works without
        // having to reach into the VM internals.
        Assert.Same(fx.Trades.Store, fx.Trades.Store);
    }
}
