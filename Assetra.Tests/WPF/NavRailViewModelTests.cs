using Assetra.WPF.Shell;
using Xunit;

namespace Assetra.Tests.WPF;

public sealed class NavRailViewModelTests
{
    [Theory]
    [InlineData(NavSection.Categories, NavSection.Cashflow)]
    [InlineData(NavSection.Recurring, NavSection.Cashflow)]
    [InlineData(NavSection.Goals, NavSection.Insights)]
    [InlineData(NavSection.Trends, NavSection.Insights)]
    [InlineData(NavSection.Reports, NavSection.Insights)]
    [InlineData(NavSection.Fire, NavSection.Insights)]
    [InlineData(NavSection.MonteCarlo, NavSection.Insights)]
    [InlineData(NavSection.RealEstate, NavSection.MultiAsset)]
    [InlineData(NavSection.Insurance, NavSection.MultiAsset)]
    [InlineData(NavSection.Retirement, NavSection.MultiAsset)]
    [InlineData(NavSection.PhysicalAsset, NavSection.MultiAsset)]
    public void SelectedRailSection_MapsLegacyRoutesToParentHub(
        NavSection legacyRoute,
        NavSection expectedRailSection)
    {
        var vm = new NavRailViewModel();

        vm.ActiveSection = legacyRoute;

        Assert.Equal(legacyRoute, vm.ActiveSection);
        Assert.Equal(expectedRailSection, vm.SelectedRailSection);
    }

    [Fact]
    public void SelectedRailSection_Setter_NavigatesToHubRoute()
    {
        var vm = new NavRailViewModel
        {
            ActiveSection = NavSection.Categories,
        };

        vm.SelectedRailSection = NavSection.Cashflow;

        Assert.Equal(NavSection.Cashflow, vm.ActiveSection);
        Assert.Equal(NavSection.Cashflow, vm.SelectedRailSection);
    }
}
