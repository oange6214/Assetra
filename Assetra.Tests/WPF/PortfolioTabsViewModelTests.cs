using Assetra.Core.Models;
using Assetra.WPF.Features.Portfolio.SubViewModels;
using Xunit;

namespace Assetra.Tests.WPF;

public class PortfolioTabsViewModelTests
{
    // PortfolioGroup is a positional record: Id, Name, ColorHex?, Description?, IconKey?, SortOrder, DefaultCashAccountId?, IsSystem
    // IsSystem is the 8th param (index 7), so we must use the named argument to skip optional positional params.
    private static PortfolioGroup User(string name) => new(Guid.NewGuid(), name, "#3B82F6");
    private static PortfolioGroup SystemDefault() =>
        new(PortfolioGroup.DefaultId, "預設群組", IsSystem: true);

    [Fact]
    public void Ctor_NoPositionInfo_OmitsUngroupedTab()
    {
        // WHY: at construction there's no position info yet, so the「未指定組合」bucket tab is not
        // offered — it would be an empty bucket. The post-load Sync(showUngrouped:true) adds it only
        // when ungrouped positions actually exist.
        var vm = new PortfolioTabsViewModel(
            [SystemDefault(), User("退休"), User("買房")],
            allLabel: "全部", ungroupedLabel: "未指定組合");

        Assert.Equal(3, vm.Tabs.Count);                       // 全部, 退休, 買房（無未指定）
        Assert.True(vm.Tabs[0].IsAll);
        Assert.DoesNotContain(vm.Tabs, t => t.GroupId == PortfolioGroup.DefaultId);
        Assert.Same(vm.Tabs[0], vm.SelectedTab);
        Assert.Null(vm.SelectedGroupId);
    }

    [Fact]
    public void Sync_ShowUngrouped_InsertsUngroupedTabRightAfterAll()
    {
        // WHY: when ungrouped positions exist, the「未指定組合」tab must appear in the canonical
        // position — immediately after 全部 and before the user groups.
        var vm = new PortfolioTabsViewModel(
            [SystemDefault(), User("退休"), User("買房")], "全部", "未指定組合");

        vm.Sync([SystemDefault(), User("退休"), User("買房")], "全部", "未指定組合", showUngrouped: true);

        Assert.Equal(4, vm.Tabs.Count);                       // 全部, 未指定組合, 退休, 買房
        Assert.True(vm.Tabs[0].IsAll);
        Assert.Equal(PortfolioGroup.DefaultId, vm.Tabs[1].GroupId);
        Assert.Equal("未指定組合", vm.Tabs[1].Name);
    }

    [Fact]
    public void NoUserGroups_OnlyAllTab()
    {
        var vm = new PortfolioTabsViewModel([SystemDefault()], "全部", "未指定組合");
        Assert.Single(vm.Tabs);
        Assert.True(vm.Tabs[0].IsAll);
    }

    [Fact]
    public void SelectingGroupTab_ExposesItsGroupId()
    {
        var a = User("退休");
        var vm = new PortfolioTabsViewModel([SystemDefault(), a], "全部", "未指定組合");
        vm.SelectedTab = vm.Tabs[^1];
        Assert.Equal(a.Id, vm.SelectedGroupId);
    }

    [Fact]
    public void Sync_PreservesSelectionByGroupId()
    {
        var a = User("退休");
        var vm = new PortfolioTabsViewModel([SystemDefault(), a], "全部", "未指定組合");
        vm.SelectedTab = vm.Tabs[^1];
        vm.Sync([SystemDefault(), a, User("買房")], "全部", "未指定組合", showUngrouped: true);
        Assert.Equal(a.Id, vm.SelectedGroupId);
    }
}
