using Assetra.WPF.Shell;
using Xunit;

namespace Assetra.Tests.WPF;

public sealed class NavRailViewModelTests
{
    [Fact]
    public void ActiveSection_DefaultsToPortfolio()
    {
        var vm = new NavRailViewModel();

        Assert.Equal(NavSection.Portfolio, vm.ActiveSection);
    }

    [Fact]
    public void ActiveSection_Setter_RoutesThroughNavigateTo()
    {
        var vm = new NavRailViewModel();

        vm.ActiveSection = NavSection.Settings;

        Assert.Equal(NavSection.Settings, vm.ActiveSection);
        Assert.True(vm.CanGoBack);
        Assert.False(vm.CanGoForward);
    }

    [Fact]
    public void NavigateTo_SameSection_IsNoOp()
    {
        var vm = new NavRailViewModel();
        vm.NavigateTo(NavSection.Portfolio);

        Assert.Equal(NavSection.Portfolio, vm.ActiveSection);
        Assert.False(vm.CanGoBack);
    }

    [Fact]
    public void GoBackAndForward_TraverseHistory()
    {
        var vm = new NavRailViewModel();

        vm.ActiveSection = NavSection.Categories;
        vm.ActiveSection = NavSection.Goals;

        Assert.True(vm.CanGoBack);
        Assert.False(vm.CanGoForward);

        vm.GoBackCommand.Execute(null);
        Assert.Equal(NavSection.Categories, vm.ActiveSection);
        Assert.True(vm.CanGoBack);
        Assert.True(vm.CanGoForward);

        vm.GoBackCommand.Execute(null);
        Assert.Equal(NavSection.Portfolio, vm.ActiveSection);
        Assert.False(vm.CanGoBack);

        vm.GoForwardCommand.Execute(null);
        Assert.Equal(NavSection.Categories, vm.ActiveSection);

        vm.GoForwardCommand.Execute(null);
        Assert.Equal(NavSection.Goals, vm.ActiveSection);
        Assert.False(vm.CanGoForward);
    }

    [Fact]
    public void NavigatingFromMidHistory_ClearsForwardStack()
    {
        var vm = new NavRailViewModel();
        vm.ActiveSection = NavSection.Categories;
        vm.ActiveSection = NavSection.Goals;
        vm.GoBackCommand.Execute(null); // back to Categories
        Assert.True(vm.CanGoForward);

        vm.ActiveSection = NavSection.Reports;
        Assert.False(vm.CanGoForward);
    }

    [Fact]
    public void HistoryCommands_ExposeCanExecuteState()
    {
        var vm = new NavRailViewModel();

        Assert.False(vm.GoBackCommand.CanExecute(null));
        Assert.False(vm.GoForwardCommand.CanExecute(null));

        vm.ActiveSection = NavSection.Settings;

        Assert.True(vm.GoBackCommand.CanExecute(null));
        Assert.False(vm.GoForwardCommand.CanExecute(null));

        vm.GoBackCommand.Execute(null);

        Assert.False(vm.GoBackCommand.CanExecute(null));
        Assert.True(vm.GoForwardCommand.CanExecute(null));
    }
}
