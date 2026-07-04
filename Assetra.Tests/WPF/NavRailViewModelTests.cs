using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Shell;
using Xunit;

namespace Assetra.Tests.WPF;

public sealed class NavRailViewModelTests
{
    [Fact]
    public void Groups_RestoreExpansionFromSettings()
    {
        var settings = new FakeSettings(new AppSettings(NavExpandedGroups: "Nav.Analysis,Nav.Tools"));
        var vm = new NavRailViewModel(settings);

        Assert.True(GroupByKey(vm, "Nav.Analysis").IsExpanded);
        Assert.True(GroupByKey(vm, "Nav.Tools").IsExpanded);
        Assert.False(GroupByKey(vm, "Nav.Assets").IsExpanded);
        Assert.False(GroupByKey(vm, "Nav.Planning").IsExpanded);
    }

    [Fact]
    public void TogglingGroup_PersistsExpandedSet()
    {
        var settings = new FakeSettings(new AppSettings());
        var vm = new NavRailViewModel(settings);
        var planning = GroupByKey(vm, "Nav.Planning"); // default collapsed

        planning.ToggleExpandedCommand.Execute(null);

        Assert.Contains("Nav.Planning", settings.Current.NavExpandedGroups.Split(','));
    }

    private static NavGroupVm GroupByKey(NavRailViewModel vm, string key)
        => vm.Groups.First(g => g.TitleResourceKey == key);

    /// <summary>
    /// Minimal in-file fake mirroring the real service: <see cref="Current"/> holds the
    /// seeded record and <see cref="SaveAsync"/> applies the mutation in place, honouring
    /// <paramref name="raiseChanged"/> (bookkeeping saves pass false → no Changed).
    /// </summary>
    private sealed class FakeSettings(AppSettings initial) : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = initial;
        public event Action? Changed;

        public Task SaveAsync(AppSettings settings, bool raiseChanged = true)
        {
            Current = settings;
            if (raiseChanged)
                Changed?.Invoke();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void Groups_SplitCoreAndAdvanced_WithDefaultExpansion()
    {
        var vm = new NavRailViewModel();

        Assert.Collection(vm.Groups,
            g => AssertGroup(g, "Nav.Analysis",   true,  NavSection.FinancialOverview, NavSection.Reports, NavSection.Assistant),
            g => AssertGroup(g, "Nav.Assets",     true,  NavSection.Portfolio, NavSection.CashAccounts, NavSection.Liabilities),
            g => AssertGroup(g, "Nav.Cashflow",   true,  NavSection.Categories, NavSection.Recurring, NavSection.TransactionLog, NavSection.Alerts),
            g => AssertGroup(g, "Nav.MoreAssets", false, NavSection.RealEstate, NavSection.Insurance, NavSection.Retirement, NavSection.PhysicalAsset),
            g => AssertGroup(g, "Nav.Planning",   false, NavSection.Goals, NavSection.Fire, NavSection.MonteCarlo, NavSection.Calculators),
            g => AssertGroup(g, "Nav.Tools",      false, NavSection.AuditLog));
    }

    private static void AssertGroup(NavGroupVm g, string titleKey, bool expanded, params NavSection[] sections)
    {
        Assert.Equal(titleKey, g.TitleResourceKey);
        Assert.Equal(expanded, g.IsExpanded);
        Assert.Equal(sections, g.Items.Select(i => i.Section).ToArray());
    }

    [Fact]
    public void ActiveSection_DefaultsToFinancialOverview()
    {
        var vm = new NavRailViewModel();

        Assert.Equal(NavSection.FinancialOverview, vm.ActiveSection);
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
        vm.NavigateTo(NavSection.FinancialOverview);

        Assert.Equal(NavSection.FinancialOverview, vm.ActiveSection);
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
        Assert.Equal(NavSection.FinancialOverview, vm.ActiveSection);
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
