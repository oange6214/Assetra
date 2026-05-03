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

    [Theory]
    [InlineData(NavSection.Cashflow, NavSection.Categories)]
    [InlineData(NavSection.Categories, NavSection.Categories)]
    [InlineData(NavSection.Recurring, NavSection.Recurring)]
    public void SelectedCashflowTab_TracksParentAndLegacyRoutes(
        NavSection route,
        NavSection expectedTab)
    {
        var vm = new NavRailViewModel();

        vm.ActiveSection = route;

        Assert.Equal(expectedTab, vm.SelectedCashflowTab);
    }

    [Theory]
    [InlineData(NavSection.Insights, NavSection.Goals)]
    [InlineData(NavSection.Goals, NavSection.Goals)]
    [InlineData(NavSection.Trends, NavSection.Trends)]
    [InlineData(NavSection.Reports, NavSection.Reports)]
    [InlineData(NavSection.Fire, NavSection.Fire)]
    [InlineData(NavSection.MonteCarlo, NavSection.MonteCarlo)]
    public void SelectedInsightsTab_TracksParentAndLegacyRoutes(
        NavSection route,
        NavSection expectedTab)
    {
        var vm = new NavRailViewModel();

        vm.ActiveSection = route;

        Assert.Equal(expectedTab, vm.SelectedInsightsTab);
    }

    [Theory]
    [InlineData(NavSection.MultiAsset, NavSection.RealEstate)]
    [InlineData(NavSection.RealEstate, NavSection.RealEstate)]
    [InlineData(NavSection.Insurance, NavSection.Insurance)]
    [InlineData(NavSection.Retirement, NavSection.Retirement)]
    [InlineData(NavSection.PhysicalAsset, NavSection.PhysicalAsset)]
    public void SelectedMultiAssetTab_TracksParentAndLegacyRoutes(
        NavSection route,
        NavSection expectedTab)
    {
        var vm = new NavRailViewModel();

        vm.ActiveSection = route;

        Assert.Equal(expectedTab, vm.SelectedMultiAssetTab);
    }

    [Fact]
    public void SelectedHubTab_Setters_NavigateToLegacyRoute()
    {
        var vm = new NavRailViewModel();

        vm.SelectedCashflowTab = NavSection.Recurring;
        Assert.Equal(NavSection.Recurring, vm.ActiveSection);

        vm.SelectedInsightsTab = NavSection.Reports;
        Assert.Equal(NavSection.Reports, vm.ActiveSection);

        vm.SelectedMultiAssetTab = NavSection.Insurance;
        Assert.Equal(NavSection.Insurance, vm.ActiveSection);
    }

    [Fact]
    public void HistoryCommands_ExposeCanExecuteState()
    {
        var vm = new NavRailViewModel();

        Assert.False(vm.CanGoBack);
        Assert.False(vm.CanGoForward);
        Assert.False(vm.GoBackCommand.CanExecute(null));
        Assert.False(vm.GoForwardCommand.CanExecute(null));

        vm.ActiveSection = NavSection.Settings;

        Assert.True(vm.CanGoBack);
        Assert.False(vm.CanGoForward);
        Assert.True(vm.GoBackCommand.CanExecute(null));
        Assert.False(vm.GoForwardCommand.CanExecute(null));

        vm.GoBackCommand.Execute(null);

        Assert.False(vm.CanGoBack);
        Assert.True(vm.CanGoForward);
        Assert.False(vm.GoBackCommand.CanExecute(null));
        Assert.True(vm.GoForwardCommand.CanExecute(null));
    }
}
