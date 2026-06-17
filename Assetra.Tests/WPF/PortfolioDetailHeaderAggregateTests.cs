using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure;
using Assetra.Infrastructure.Persistence;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Features.Portfolio.SubViewModels;
using Assetra.WPF.Features.PortfolioGroups;
using Assetra.WPF.Infrastructure;
using Assetra.Tests.WPF.Fixtures;
using Moq;
using Xunit;

namespace Assetra.Tests.WPF;

/// <summary>
/// WHY: Task 1.4 — verifies that SelectedPortfolio* aggregates correctly reflect the
/// rows that belong to the selected tab's portfolio group, and that switching to 全部
/// reverts to the whole-portfolio totals already computed by RebuildTotals.
/// </summary>
public class PortfolioDetailHeaderAggregateTests
{
    private static PortfolioEntry MakeEntry(
        string symbol,
        Guid groupId,
        decimal price,
        int qty,
        string currency = "TWD")
        => new(Guid.NewGuid(), symbol, "TWSE", Currency: currency, PortfolioGroupId: groupId);

    private static Dictionary<Guid, PositionSnapshot> SnapshotsFor(
        IReadOnlyList<(PortfolioEntry Entry, decimal Price, int Qty)> items) =>
        items.ToDictionary(
            x => x.Entry.Id,
            x => new PositionSnapshot(
                x.Entry.Id,
                x.Qty,
                x.Price * x.Qty,   // TotalCost
                x.Price,            // AverageCost
                0m,
                DateOnly.FromDateTime(DateTime.Today)));

    private static Mock<IStockService> SilentStockService()
    {
        var mock = new Mock<IStockService>();
        mock.Setup(s => s.QuoteStream)
            .Returns(Observable.Never<IReadOnlyList<StockQuote>>());
        mock.Setup(s => s.RefreshNowAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static PortfolioViewModel CreateVm(
        IReadOnlyList<PortfolioEntry> entries,
        IPositionQueryService positionQuery,
        PortfolioGroupCatalog catalog,
        IAppSettingsService? settings = null)
    {
        var repo = new Mock<IPortfolioRepository>();
        repo.Setup(r => r.GetEntriesAsync()).ReturnsAsync(entries.ToList());

        var snapshotRepo = new Mock<IPortfolioSnapshotRepository>();
        snapshotRepo.Setup(r => r.GetSnapshotsAsync(It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>()))
            .ReturnsAsync(Array.Empty<PortfolioDailySnapshot>());
        snapshotRepo.Setup(r => r.UpsertAsync(It.IsAny<PortfolioDailySnapshot>()))
            .Returns(Task.CompletedTask);

        var logRepo = new Mock<IPortfolioPositionLogRepository>();
        logRepo.Setup(r => r.HasAnyAsync()).ReturnsAsync(true);
        logRepo.Setup(r => r.LogAsync(It.IsAny<PortfolioPositionLog>())).Returns(Task.CompletedTask);
        logRepo.Setup(r => r.LogBatchAsync(It.IsAny<IEnumerable<PortfolioPositionLog>>())).Returns(Task.CompletedTask);
        logRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<PortfolioPositionLog>());

        var historyProvider = new Mock<IStockHistoryProvider>();
        var snapshotSvc = new PortfolioSnapshotService(snapshotRepo.Object);
        var backfill = new PortfolioBackfillService(logRepo.Object, snapshotRepo.Object, historyProvider.Object);
        var fakeTradeRepo = new FakeTradeRepo();

        return new PortfolioViewModel(
            new PortfolioRepositories(repo.Object, snapshotRepo.Object, logRepo.Object, Trade: fakeTradeRepo),
            new PortfolioServices(SilentStockService().Object, new Mock<IStockSearchService>().Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                PositionQuery: positionQuery,
                GroupCatalog: catalog),
            new PortfolioUiServices(ImmediateScheduler.Instance, Settings: settings));
    }

    private sealed class FakeGroupRepo(IReadOnlyList<PortfolioGroup> groups) : IPortfolioGroupRepository
    {
        public Task<IReadOnlyList<PortfolioGroup>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(groups);
        public Task<PortfolioGroup?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(groups.FirstOrDefault(g => g.Id == id));
        public Task AddAsync(PortfolioGroup group, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(PortfolioGroup group, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task SelectingPortfolioTab_SetsAggregatesFromFilteredRows()
    {
        // WHY: ensures that SelectedPortfolioMarketValue/Pnl only reflect the rows whose
        // PortfolioGroupId matches the selected tab, not the whole portfolio.
        var alphaId = Guid.NewGuid();
        var betaId = Guid.NewGuid();
        var catalog = new PortfolioGroupCatalog(new FakeGroupRepo([
            new PortfolioGroup(PortfolioGroup.DefaultId, "預設", IsSystem: true),
            new PortfolioGroup(alphaId, "Alpha"),
            new PortfolioGroup(betaId, "Beta"),
        ]));

        var entryAlpha = MakeEntry("2330", alphaId, 500m, 10);
        var entryBeta = MakeEntry("0050", betaId, 100m, 20);
        var snapshots = SnapshotsFor([(entryAlpha, 500m, 10), (entryBeta, 100m, 20)]);

        var positionQuery = new Mock<IPositionQueryService>();
        positionQuery.Setup(s => s.GetAllPositionSnapshotsAsync()).ReturnsAsync(snapshots);
        positionQuery.Setup(s => s.GetPositionAsync(It.IsAny<Guid>()))
            .Returns<Guid>(id => Task.FromResult(snapshots.TryGetValue(id, out var s) ? s : null));
        positionQuery.Setup(s => s.ComputeRealizedPnlAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<decimal>(),
                It.IsAny<decimal>(), It.IsAny<decimal>()))
            .ReturnsAsync(0m);

        var vm = CreateVm([entryAlpha, entryBeta], positionQuery.Object, catalog);
        await vm.LoadAsync();

        Assert.Equal(2, vm.Positions.Count);

        // Select the Alpha tab.
        var alphaTab = vm.PortfolioTabs.Tabs.FirstOrDefault(t => t.GroupId == alphaId);
        Assert.NotNull(alphaTab);
        vm.PortfolioTabs.SelectedTab = alphaTab;

        // WHY: Alpha has 10 shares × 500 = 5000 cost; Beta (2000) rows must not contribute.
        // MarketValue is 0 in tests since no live price feed; Cost uses BuyPrice × Qty.
        Assert.Equal("Alpha", vm.SelectedPortfolioName);
        Assert.Equal(5000m, vm.SelectedPortfolioCost);
        Assert.Equal(0m, vm.SelectedPortfolioMarketValue);
    }

    [Fact]
    public void ToggleOverview_Collapsing_AlsoClosesExpandedKpiPanel()
    {
        // WHY: the insights chips and the KPI drill-down panel live inside the collapsible
        // overview. If collapsing left ExpandedKpiPanel set, the drill-down panel would stay
        // visible with no chip to close it. Collapsing must clear it; expanding must not.
        var catalog = new PortfolioGroupCatalog(new FakeGroupRepo([
            new PortfolioGroup(PortfolioGroup.DefaultId, "預設", IsSystem: true),
        ]));
        var positionQuery = new Mock<IPositionQueryService>();
        positionQuery.Setup(s => s.GetAllPositionSnapshotsAsync())
            .ReturnsAsync(new Dictionary<Guid, PositionSnapshot>());

        var vm = CreateVm([], positionQuery.Object, catalog);

        Assert.True(vm.IsOverviewExpanded);   // default: expanded
        vm.ExpandedKpiPanel = "marketvalue";  // a drill-down is open

        vm.ToggleOverviewCommand.Execute(null);   // collapse
        Assert.False(vm.IsOverviewExpanded);
        Assert.Null(vm.ExpandedKpiPanel);         // drill-down closed with it

        vm.ToggleOverviewCommand.Execute(null);   // expand again
        Assert.True(vm.IsOverviewExpanded);
    }

    [Fact]
    public void ToggleOverview_PersistsPreference_WithoutRaisingChanged()
    {
        // WHY: the overview expand/collapse is a per-user UI preference that must survive restart,
        // so toggling persists it. Critically it must save with raiseChanged: false — a bookkeeping
        // save that triggers the app-wide Changed reload would re-introduce the settings-Changed
        // flicker loop. This test pins both: the value is persisted AND Changed is not raised.
        var catalog = new PortfolioGroupCatalog(new FakeGroupRepo([
            new PortfolioGroup(PortfolioGroup.DefaultId, "預設", IsSystem: true),
        ]));
        var positionQuery = new Mock<IPositionQueryService>();
        positionQuery.Setup(s => s.GetAllPositionSnapshotsAsync())
            .ReturnsAsync(new Dictionary<Guid, PositionSnapshot>());

        var settings = new Mock<IAppSettingsService>();
        settings.SetupGet(s => s.Current).Returns(new AppSettings());
        AppSettings? saved = null;
        bool? raised = null;
        settings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>(), It.IsAny<bool>()))
            .Callback<AppSettings, bool>((s, r) => { saved = s; raised = r; })
            .Returns(Task.CompletedTask);

        var vm = CreateVm([], positionQuery.Object, catalog, settings.Object);

        Assert.True(vm.IsOverviewExpanded);   // default expanded
        vm.ToggleOverviewCommand.Execute(null);   // collapse

        Assert.False(vm.IsOverviewExpanded);
        Assert.NotNull(saved);
        Assert.False(saved!.PortfolioOverviewExpanded);   // collapsed state persisted
        Assert.False(raised!.Value);                       // raiseChanged: false — no flicker loop
    }

    [Fact]
    public async Task SelectingAllTab_ReusesWholePorfoliTotals()
    {
        // WHY: 全部 tab must not sum rows independently (which would double-count base vs native
        // conversions for multi-currency); it must reuse TotalMarketValue already set by RebuildTotals.
        var groupId = Guid.NewGuid();
        var catalog = new PortfolioGroupCatalog(new FakeGroupRepo([
            new PortfolioGroup(PortfolioGroup.DefaultId, "預設", IsSystem: true),
            new PortfolioGroup(groupId, "Test"),
        ]));

        var entry = MakeEntry("2330", groupId, 500m, 10);
        var snapshots = SnapshotsFor([(entry, 500m, 10)]);

        var positionQuery = new Mock<IPositionQueryService>();
        positionQuery.Setup(s => s.GetAllPositionSnapshotsAsync()).ReturnsAsync(snapshots);
        positionQuery.Setup(s => s.GetPositionAsync(It.IsAny<Guid>()))
            .Returns<Guid>(id => Task.FromResult(snapshots.TryGetValue(id, out var s) ? s : null));
        positionQuery.Setup(s => s.ComputeRealizedPnlAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<decimal>(),
                It.IsAny<decimal>(), It.IsAny<decimal>()))
            .ReturnsAsync(0m);

        var vm = CreateVm([entry], positionQuery.Object, catalog);
        await vm.LoadAsync();

        // Switch to a portfolio tab first, then switch back to 全部.
        var portfolioTab = vm.PortfolioTabs.Tabs.FirstOrDefault(t => t.GroupId == groupId);
        Assert.NotNull(portfolioTab);
        vm.PortfolioTabs.SelectedTab = portfolioTab;
        var nameBeforeAll = vm.SelectedPortfolioName;
        Assert.Equal("Test", nameBeforeAll);

        // Switch back to 全部 (first tab, IsAll = true, GroupId = null).
        var allTab = vm.PortfolioTabs.Tabs.First(t => t.IsAll);
        vm.PortfolioTabs.SelectedTab = allTab;

        // WHY: 全部 tab shows the whole-portfolio total from RebuildTotals, not a filtered sum.
        Assert.Null(vm.PortfolioTabs.SelectedGroupId);
        Assert.Equal(vm.TotalMarketValue, vm.SelectedPortfolioMarketValue);
        Assert.True(vm.IsSelectedPortfolioHistoryVisible);
        Assert.False(vm.IsSelectedPortfolioTrendVisible);
    }
}
