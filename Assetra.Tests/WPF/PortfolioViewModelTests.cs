using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Windows.Data;
using Assetra.Application.Loans.Services;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure;
using Assetra.Infrastructure.Persistence;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Features.Portfolio.Controls;
using Assetra.WPF.Features.Portfolio.SubViewModels;
using Assetra.WPF.Features.PortfolioGroups;
using Assetra.WPF.Infrastructure;
using Moq;
using Xunit;

namespace Assetra.Tests.WPF;

public class PortfolioViewModelTests
{
    private static PortfolioEntry MakeEntry(string symbol = "2330", decimal price = 100m, int qty = 1000) =>
        new(Guid.NewGuid(), symbol, "TWSE");

    /// <summary>
    /// Builds a dictionary of PositionSnapshots matching what MakeEntry would have stored.
    /// Used to wire a fake IPositionQueryService in tests that assert financial totals.
    /// </summary>
    private static Dictionary<Guid, PositionSnapshot> SnapshotsFor(
        IReadOnlyList<(PortfolioEntry Entry, decimal Price, int Qty)> items) =>
        items.ToDictionary(
            x => x.Entry.Id,
            x => new PositionSnapshot(
                x.Entry.Id,
                x.Qty,
                x.Price * x.Qty,
                x.Price,
                0m,
                DateOnly.FromDateTime(DateTime.Today)));

    private static Mock<IPositionQueryService> PositionQueryMock(
        Dictionary<Guid, PositionSnapshot> snapshots)
    {
        var mock = new Mock<IPositionQueryService>();
        mock.Setup(s => s.GetAllPositionSnapshotsAsync())
            .ReturnsAsync(snapshots);
        mock.Setup(s => s.GetPositionAsync(It.IsAny<Guid>()))
            .Returns<Guid>(id => Task.FromResult(
                snapshots.TryGetValue(id, out var s) ? s : null));
        mock.Setup(s => s.ComputeRealizedPnlAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<decimal>(),
                It.IsAny<decimal>(), It.IsAny<decimal>()))
            .ReturnsAsync(0m);
        return mock;
    }

    private static (PortfolioSnapshotService svc, Mock<IPortfolioSnapshotRepository> repo) SnapshotStubs()
    {
        var repo = new Mock<IPortfolioSnapshotRepository>();
        repo.Setup(r => r.GetSnapshotsAsync(It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>()))
            .ReturnsAsync(Array.Empty<PortfolioDailySnapshot>());
        repo.Setup(r => r.UpsertAsync(It.IsAny<PortfolioDailySnapshot>()))
            .Returns(Task.CompletedTask);
        return (new PortfolioSnapshotService(repo.Object), repo);
    }

    // IStockService stub whose QuoteStream never emits — keeps tests focused on non-price logic
    private static Mock<IStockService> SilentStockService()
    {
        var mock = new Mock<IStockService>();
        mock.Setup(s => s.QuoteStream)
            .Returns(Observable.Never<IReadOnlyList<StockQuote>>());
        mock.Setup(s => s.RefreshNowAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    private static (Mock<IPortfolioPositionLogRepository> logRepo, PortfolioBackfillService backfill)
        BackfillStubs(Mock<IPortfolioSnapshotRepository> snapshotRepo)
    {
        var logRepo = new Mock<IPortfolioPositionLogRepository>();
        logRepo.Setup(r => r.HasAnyAsync()).ReturnsAsync(true);
        logRepo.Setup(r => r.LogAsync(It.IsAny<PortfolioPositionLog>())).Returns(Task.CompletedTask);
        logRepo.Setup(r => r.LogBatchAsync(It.IsAny<IEnumerable<PortfolioPositionLog>>())).Returns(Task.CompletedTask);
        logRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<PortfolioPositionLog>());

        var historyProvider = new Mock<IStockHistoryProvider>();
        var backfill = new PortfolioBackfillService(logRepo.Object, snapshotRepo.Object, historyProvider.Object);
        return (logRepo, backfill);
    }

    private (PortfolioViewModel vm, Mock<IPortfolioRepository> repo) CreateVm(
        IReadOnlyList<PortfolioEntry>? entries = null,
        IPositionQueryService? positionQuery = null,
        PortfolioGroupCatalog? groupCatalog = null,
        IReadOnlyList<Trade>? trades = null)
    {
        var mutableEntries = (entries ?? [MakeEntry()]).ToList();
        var repo = new Mock<IPortfolioRepository>();
        repo.Setup(r => r.GetEntriesAsync()).ReturnsAsync(() => mutableEntries.ToList());
        repo.Setup(r => r.AddAsync(It.IsAny<PortfolioEntry>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<PortfolioEntry>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.RemoveAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.ArchiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) =>
            {
                var idx = mutableEntries.FindIndex(e => e.Id == id);
                if (idx >= 0)
                    mutableEntries[idx] = mutableEntries[idx] with { IsActive = false };
            })
            .Returns(Task.CompletedTask);

        var search = new Mock<IStockSearchService>();
        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);

        var fakeTradeRepo = new FakeTradeRepo();
        if (trades is not null)
            fakeTradeRepo.Store.AddRange(trades);
        var vm = new PortfolioViewModel(
            new PortfolioRepositories(repo.Object, snapshotRepo.Object, logRepo.Object, Trade: fakeTradeRepo),
            new PortfolioServices(SilentStockService().Object, search.Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                PositionQuery: positionQuery,
                GroupCatalog: groupCatalog),
            new PortfolioUiServices(ImmediateScheduler.Instance));
        return (vm, repo);
    }

    private sealed class FakePortfolioGroupRepo(IReadOnlyList<PortfolioGroup> groups) : IPortfolioGroupRepository
    {
        public Task<IReadOnlyList<PortfolioGroup>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(groups);
        public Task<PortfolioGroup?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(groups.FirstOrDefault(g => g.Id == id));
        public Task AddAsync(PortfolioGroup group, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(PortfolioGroup group, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    // LoadAsync

    [Fact]
    public async Task LoadAsync_PopulatesPositions()
    {
        var (vm, _) = CreateVm([MakeEntry("2330"), MakeEntry("2317")]);
        await vm.LoadAsync();
        Assert.Equal(2, vm.Positions.Count);
        Assert.Contains(vm.Positions, p => p.Symbol == "2330");
        Assert.Contains(vm.Positions, p => p.Symbol == "2317");
    }

    [Fact]
    public async Task SelectingInvestmentAsset_SyncsSellAndDividendPosition()
    {
        // WHY: 賣出/現金股利移除「股票」下拉後，持倉改由上方「選擇資產」cascade 設定；
        //      若沒設好，confirm 會找不到持倉、預覽也算不出來。
        var (vm, _) = CreateVm([MakeEntry("2330")]);
        await vm.LoadAsync();
        var pos = vm.Positions.Single(p => p.Symbol == "2330");

        vm.Transaction.SelectedAsset = new TxAssetSubject(
            Kind: TxAssetKind.Stock,
            Id: pos.Id,
            PrimaryName: pos.Name,
            SecondaryLine: "TWD",
            GroupKey: "Portfolio.Tx.Asset.Group.Investment",
            Currency: "TWD");

        Assert.Equal(pos.Id, vm.Transaction.Sell.Position?.Id);
        Assert.Equal(pos.Id, vm.Transaction.Div.Position?.Id);        // 現金股利
        Assert.Equal(pos.Id, vm.Transaction.Div.StockPosition?.Id);   // 股票股利
    }

    [Fact]
    public async Task LoadAsync_UsesPortfolioEntryGroupForPositionRow()
    {
        var groupId = Guid.NewGuid();
        var entry = new PortfolioEntry(Guid.NewGuid(), "0056", "TWSE", PortfolioGroupId: groupId);
        var snapshots = SnapshotsFor([(entry, 35m, 1000)]);
        var (vm, _) = CreateVm([entry], PositionQueryMock(snapshots).Object);

        await vm.LoadAsync();

        var row = Assert.Single(vm.Positions);
        Assert.Equal(groupId, row.PortfolioGroupId);
        Assert.False(row.HasPortfolioGroupConflict);
    }

    [Fact]
    public async Task LoadAsync_MarksAggregatedPositionWhenEntryGroupsConflict()
    {
        var firstGroup = Guid.NewGuid();
        var secondGroup = Guid.NewGuid();
        var first = new PortfolioEntry(Guid.NewGuid(), "0056", "TWSE", AssetType.Stock, "元大高股息", PortfolioGroupId: firstGroup);
        var second = new PortfolioEntry(Guid.NewGuid(), "0056", "TWSE", AssetType.Stock, "元大高股息", PortfolioGroupId: secondGroup);
        var snapshots = SnapshotsFor([(first, 35m, 1000), (second, 36m, 1000)]);
        var (vm, _) = CreateVm([first, second], PositionQueryMock(snapshots).Object);

        await vm.LoadAsync();

        var row = Assert.Single(vm.Positions);
        Assert.Null(row.PortfolioGroupId);
        Assert.True(row.HasPortfolioGroupConflict);
    }

    [Fact]
    public async Task SaveEditAsset_WithGroupConflict_ReassignsEveryLotToSelectedGroup()
    {
        var firstGroup = Guid.NewGuid();
        var secondGroup = Guid.NewGuid();
        var targetGroup = Guid.NewGuid();
        var catalog = new PortfolioGroupCatalog(new FakePortfolioGroupRepo([
            new PortfolioGroup(PortfolioGroup.DefaultId, "預設", IsSystem: true),
            new PortfolioGroup(firstGroup, "長期投資"),
            new PortfolioGroup(secondGroup, "短線"),
            new PortfolioGroup(targetGroup, "現金流"),
        ]));
        var first = new PortfolioEntry(Guid.NewGuid(), "0056", "TWSE", AssetType.Stock, "元大高股息", PortfolioGroupId: firstGroup);
        var second = new PortfolioEntry(Guid.NewGuid(), "0056", "TWSE", AssetType.Stock, "元大高股息", PortfolioGroupId: secondGroup);
        var snapshots = SnapshotsFor([(first, 30m, 10), (second, 30m, 20)]);
        var updated = new List<PortfolioEntry>();
        var (vm, repo) = CreateVm([first, second], PositionQueryMock(snapshots).Object, catalog);
        repo.Setup(r => r.UpdateMetadataAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<PortfolioEntry>(), It.IsAny<CancellationToken>()))
            .Callback<PortfolioEntry, CancellationToken>((entry, _) => updated.Add(entry))
            .Returns(Task.CompletedTask);

        await vm.LoadAsync();
        var row = Assert.Single(vm.Positions);

        vm.Account.OpenEditPositionCommand.Execute(row);
        Assert.True(vm.Account.EditAssetHasPortfolioGroupConflict);
        Assert.True(vm.Account.CanEditPositionGroup);
        Assert.Equal(PortfolioGroup.DefaultId, vm.Account.EditAssetPortfolioGroupId);
        Assert.Equal("元大高股息", vm.Account.EditAssetName);

        vm.Account.EditAssetPortfolioGroupId = targetGroup;
        await vm.Account.SaveEditAssetCommand.ExecuteAsync(null);

        Assert.Empty(vm.Account.EditAssetError);
        Assert.False(vm.Account.IsEditAssetDialogOpen);
        Assert.False(vm.Account.EditAssetHasPortfolioGroupConflict);
        Assert.NotEmpty(updated);
        Assert.False(row.HasPortfolioGroupConflict);
        Assert.Equal(targetGroup, row.PortfolioGroupId);
        Assert.Equal("現金流", row.PortfolioGroupDisplay);
        Assert.Equal(new[] { first.Id, second.Id }, updated.Select(entry => entry.Id));
        Assert.All(updated, entry => Assert.Equal(targetGroup, entry.PortfolioGroupId));
    }

    [Fact]
    public async Task ShowClosedPositions_DefaultHidesSoldOut_ToggleReveals()
    {
        // WHY: a sold-out (qty 0) holding is not a current holding — the list should default to
        // showing only what you still hold, so 筆數/市值 stay clean. The single "顯示已平倉" toggle
        // brings closed positions back (the entry is retained, so it also reappears if re-bought).
        var catalog = new PortfolioGroupCatalog(new FakePortfolioGroupRepo([
            new PortfolioGroup(PortfolioGroup.DefaultId, "預設", IsSystem: true),
        ]));
        var held = new PortfolioEntry(Guid.NewGuid(), "2330", "TWSE", AssetType.Stock, "台積電");
        // Sold-out: the sell flow archives the entry (IsActive=false) once fully sold.
        var soldOut = new PortfolioEntry(Guid.NewGuid(), "0050", "TWSE", AssetType.Stock, "元大台灣50", IsActive: false);
        // Watchlist ("+觀察"): never traded → no snapshot → must stay visible (NOT closed).
        var watchlist = new PortfolioEntry(Guid.NewGuid(), "2317", "TWSE", AssetType.Stock, "鴻海");
        var snapshots = SnapshotsFor([(held, 600m, 10), (soldOut, 100m, 0)]);
        var (vm, _) = CreateVm([held, soldOut, watchlist], PositionQueryMock(snapshots).Object, catalog);

        await vm.LoadAsync();

        // Default: the archived sold-out position is hidden; held + watchlist stay visible.
        Assert.False(vm.ShowClosedPositions);
        Assert.DoesNotContain(vm.Positions, p => p.Symbol == "0050");
        Assert.Contains(vm.Positions, p => p.Symbol == "2330");
        Assert.Contains(vm.Positions, p => p.Symbol == "2317");

        // Toggle on, reload: the sold-out (archived) position re-appears.
        vm.ShowClosedPositions = true;
        await vm.LoadAsync();

        Assert.Contains(vm.Positions, p => p.Symbol == "0050");
    }

    [Fact]
    public async Task LoadAsync_DefaultsToAllTabWhenCustomGroupsExist()
    {
        // WHY: after Task 1.6, ViewMode is gone. When user has custom groups the tab strip
        // should populate with those groups but SelectedTab should still start on 全部
        // (no auto-switch to a group tab on load — user drives navigation via tabs explicitly).
        var groupId = Guid.NewGuid();
        var catalog = new PortfolioGroupCatalog(new FakePortfolioGroupRepo([
            new PortfolioGroup(PortfolioGroup.DefaultId, "預設", IsSystem: true),
            new PortfolioGroup(groupId, "長期投資"),
        ]));
        var entry = new PortfolioEntry(Guid.NewGuid(), "0056", "TWSE", PortfolioGroupId: groupId);
        var snapshots = SnapshotsFor([(entry, 35m, 1000)]);
        var (vm, _) = CreateVm([entry], PositionQueryMock(snapshots).Object, catalog);

        await vm.LoadAsync();

        // Tab strip should include: 全部, 未指定組合, 長期投資
        Assert.Contains(vm.PortfolioTabs.Tabs, t => t.IsAll);
        Assert.Contains(vm.PortfolioTabs.Tabs, t => t.GroupId == groupId);
        // SelectedTab defaults to 全部 (no auto-navigate on load)
        Assert.True(vm.PortfolioTabs.SelectedTab?.IsAll ?? false);
        // PositionsView has no group grouping — flat view (ViewMode machinery removed)
        Assert.Empty(vm.PositionsView.GroupDescriptions);
    }

    [Fact]
    public async Task OpenEditPosition_UsesAssetDialogCopyAndKeepsGroupSelection()
    {
        var groupId = Guid.NewGuid();
        var catalog = new PortfolioGroupCatalog(new FakePortfolioGroupRepo([
            new PortfolioGroup(PortfolioGroup.DefaultId, "預設", IsSystem: true),
            new PortfolioGroup(groupId, "長期投資"),
        ]));
        var entry = new PortfolioEntry(Guid.NewGuid(), "0056", "TWSE", AssetType.Stock, "元大高股息", PortfolioGroupId: groupId);
        var snapshots = SnapshotsFor([(entry, 35m, 1000)]);
        var (vm, _) = CreateVm([entry], PositionQueryMock(snapshots).Object, catalog);

        await vm.LoadAsync();
        vm.Account.OpenEditPositionCommand.Execute(Assert.Single(vm.Positions));

        Assert.Equal("編輯資產", vm.Account.EditAssetDialogTitle);
        Assert.Equal("更新投資資產名稱、幣別與群組。", vm.Account.EditAssetDialogSubtitle);
        Assert.True(vm.Account.CanEditPositionGroup);
        Assert.Equal(groupId, vm.Account.EditAssetPortfolioGroupId);
    }

    [Fact]
    public async Task LoadAsync_BuildsTabStripFromGroupCatalog()
    {
        // WHY: SyncPortfolioTabs() must run during LoadAsync so that PortfolioTabs.Tabs
        // reflects the current catalog — including user-created portfolios. Tab strip
        // replaces the old PortfolioGroupFilterChips machinery (Task 1.6).
        var longTermId = Guid.NewGuid();
        var incomeId = Guid.NewGuid();
        var catalog = new PortfolioGroupCatalog(new FakePortfolioGroupRepo([
            new PortfolioGroup(PortfolioGroup.DefaultId, "預設", IsSystem: true),
            new PortfolioGroup(longTermId, "長期投資"),
            new PortfolioGroup(incomeId, "現金流"),
        ]));
        var entry = new PortfolioEntry(Guid.NewGuid(), "0056", "TWSE", PortfolioGroupId: longTermId);
        var snapshots = SnapshotsFor([(entry, 35m, 1000)]);
        var (vm, _) = CreateVm([entry], PositionQueryMock(snapshots).Object, catalog);

        await vm.LoadAsync();

        // Tab strip: 全部 + 長期投資 + 現金流。唯一持股已分組（longTermId）→「未指定組合」空桶
        // 不顯示（conditional ungrouped tab）。
        Assert.Equal(3, vm.PortfolioTabs.Tabs.Count);
        Assert.Contains(vm.PortfolioTabs.Tabs, t => t.IsAll);
        Assert.DoesNotContain(vm.PortfolioTabs.Tabs, t => t.GroupId == PortfolioGroup.DefaultId);
        Assert.Contains(vm.PortfolioTabs.Tabs, t => t.GroupId == longTermId);
        Assert.Contains(vm.PortfolioTabs.Tabs, t => t.GroupId == incomeId);
        // SelectedTab defaults to 全部
        Assert.True(vm.PortfolioTabs.SelectedTab?.IsAll ?? false);
    }

    [Fact]
    public async Task LoadAsync_WithUngroupedPosition_ShowsUngroupedTab()
    {
        // WHY: the「未指定組合」tab must appear exactly when an ungrouped position exists —
        // exercises ApplyPositions → SyncPortfolioTabs computing hasUngrouped from positions.
        var longTermId = Guid.NewGuid();
        var catalog = new PortfolioGroupCatalog(new FakePortfolioGroupRepo([
            new PortfolioGroup(PortfolioGroup.DefaultId, "預設", IsSystem: true),
            new PortfolioGroup(longTermId, "長期投資"),
        ]));
        var grouped = new PortfolioEntry(Guid.NewGuid(), "0056", "TWSE", PortfolioGroupId: longTermId);
        var ungrouped = new PortfolioEntry(Guid.NewGuid(), "2330", "TWSE"); // PortfolioGroupId 省略 → 未分組
        var snapshots = SnapshotsFor([(grouped, 35m, 1000), (ungrouped, 600m, 100)]);
        var (vm, _) = CreateVm([grouped, ungrouped], PositionQueryMock(snapshots).Object, catalog);

        await vm.LoadAsync();

        Assert.Contains(vm.PortfolioTabs.Tabs, t => t.GroupId == PortfolioGroup.DefaultId);
    }

    [Fact]
    public async Task SetPortfolioGroupFilter_FiltersPositionsToSelectedGroup()
    {
        var longTermId = Guid.NewGuid();
        var incomeId = Guid.NewGuid();
        var catalog = new PortfolioGroupCatalog(new FakePortfolioGroupRepo([
            new PortfolioGroup(PortfolioGroup.DefaultId, "預設", IsSystem: true),
            new PortfolioGroup(longTermId, "長期投資"),
            new PortfolioGroup(incomeId, "現金流"),
        ]));
        var longFund = new PortfolioEntry(Guid.NewGuid(), "LONGF", "", AssetType.Fund, PortfolioGroupId: longTermId);
        var incomeFund = new PortfolioEntry(Guid.NewGuid(), "INCF", "", AssetType.Fund, PortfolioGroupId: incomeId);
        var snapshots = SnapshotsFor([
            (longFund, 100m, 10),
            (incomeFund, 50m, 4),
        ]);
        var (vm, _) = CreateVm(
            [longFund, incomeFund],
            PositionQueryMock(snapshots).Object,
            catalog);

        await vm.LoadAsync();
        vm.SetPortfolioGroupFilterCommand.Execute(incomeId);

        Assert.Equal(["INCF"], vm.PositionsView.Cast<PortfolioRowViewModel>().Select(row => row.Symbol));
    }

    // ── Task 1.3 — PortfolioTabs → PortfolioGroupFilter wiring ─────────────────

    [Fact]
    public async Task SelectingGroupTab_FiltersPositionsToThatGroup()
    {
        // WHY: PortfolioTabs.SelectedTab → PortfolioGroupFilter → PositionsView.Filter must form
        // a closed loop. This test exercises the TAB path specifically (not SetPortfolioGroupFilter
        // directly) so that if the PropertyChanged subscription is missing the test fails.
        var longTermId = Guid.NewGuid();
        var incomeId = Guid.NewGuid();
        var catalog = new PortfolioGroupCatalog(new FakePortfolioGroupRepo([
            new PortfolioGroup(PortfolioGroup.DefaultId, "預設", IsSystem: true),
            new PortfolioGroup(longTermId, "長期投資"),
            new PortfolioGroup(incomeId, "現金流"),
        ]));
        var longFund  = new PortfolioEntry(Guid.NewGuid(), "LONGF", "", AssetType.Fund, PortfolioGroupId: longTermId);
        var incomeFund = new PortfolioEntry(Guid.NewGuid(), "INCF",  "", AssetType.Fund, PortfolioGroupId: incomeId);
        var snapshots = SnapshotsFor([
            (longFund,  100m, 10),
            (incomeFund, 50m,  4),
        ]);
        var (vm, _) = CreateVm(
            [longFund, incomeFund],
            PositionQueryMock(snapshots).Object,
            catalog);

        await vm.LoadAsync();

        // Select the "現金流" tab via PortfolioTabs (not SetPortfolioGroupFilterCommand).
        var incomeTab = vm.PortfolioTabs.Tabs.Single(t => t.GroupId == incomeId);
        vm.PortfolioTabs.SelectedTab = incomeTab;

        Assert.Equal(["INCF"], vm.PositionsView.Cast<PortfolioRowViewModel>().Select(r => r.Symbol));

        // Selecting the 全部 tab must restore all rows.
        var allTab = vm.PortfolioTabs.Tabs.Single(t => t.IsAll);
        vm.PortfolioTabs.SelectedTab = allTab;

        Assert.Equal(2, vm.PositionsView.Cast<PortfolioRowViewModel>().Count());
    }

    [Fact]
    public async Task OpenSelectedPositionGroupDetail_BuildsCurrentMembershipSummary()
    {
        var longTermId = Guid.NewGuid();
        var incomeId = Guid.NewGuid();
        var catalog = new PortfolioGroupCatalog(new FakePortfolioGroupRepo([
            new PortfolioGroup(PortfolioGroup.DefaultId, "預設", IsSystem: true),
            new PortfolioGroup(longTermId, "長期投資"),
            new PortfolioGroup(incomeId, "現金流"),
        ]));
        var first = new PortfolioEntry(Guid.NewGuid(), "FUND-A", "", AssetType.Fund, PortfolioGroupId: longTermId);
        var second = new PortfolioEntry(Guid.NewGuid(), "BOND-B", "", AssetType.Bond, PortfolioGroupId: longTermId);
        var other = new PortfolioEntry(Guid.NewGuid(), "INCOME", "", AssetType.Fund, PortfolioGroupId: incomeId);
        var snapshots = SnapshotsFor([
            (first, 100m, 10),
            (second, 50m, 4),
            (other, 80m, 2),
        ]);
        var (vm, _) = CreateVm(
            [first, second, other],
            PositionQueryMock(snapshots).Object,
            catalog);
        await vm.LoadAsync();
        var firstRow = vm.Positions.Single(p => p.Symbol == "FUND-A");
        var secondRow = vm.Positions.Single(p => p.Symbol == "BOND-B");
        firstRow.CurrentPrice = 120m;
        firstRow.Refresh();
        firstRow.ApplyBaseValuation("TWD", null);
        secondRow.CurrentPrice = 55m;
        secondRow.Refresh();
        secondRow.ApplyBaseValuation("TWD", null);
        vm.SelectedPositionRow = firstRow;

        var commandProperty = typeof(PortfolioViewModel)
            .GetProperty("OpenSelectedPositionGroupDetailCommand");
        Assert.NotNull(commandProperty);
        var command = Assert.IsAssignableFrom<System.Windows.Input.ICommand>(
            commandProperty!.GetValue(vm));

        command.Execute(null);

        var detailProperty = typeof(PortfolioViewModel).GetProperty("SelectedPortfolioGroupDetail");
        Assert.NotNull(detailProperty);
        var detail = detailProperty!.GetValue(vm);
        Assert.NotNull(detail);
        Assert.Equal(longTermId, detail.GetType().GetProperty("GroupId")?.GetValue(detail));
        Assert.Equal("長期投資", detail.GetType().GetProperty("Name")?.GetValue(detail));
        Assert.Equal(2, detail.GetType().GetProperty("HoldingCount")?.GetValue(detail));
        Assert.Equal(1_420m, detail.GetType().GetProperty("MarketValue")?.GetValue(detail));
        Assert.Equal(1_200m, detail.GetType().GetProperty("Cost")?.GetValue(detail));
        Assert.Equal(220m, detail.GetType().GetProperty("Pnl")?.GetValue(detail));
    }

    [Fact]
    public async Task OpenSelectedPositionGroupDetail_IncludesCurrentMembershipTrades()
    {
        var longTermId = Guid.NewGuid();
        var incomeId = Guid.NewGuid();
        var catalog = new PortfolioGroupCatalog(new FakePortfolioGroupRepo([
            new PortfolioGroup(PortfolioGroup.DefaultId, "預設", IsSystem: true),
            new PortfolioGroup(longTermId, "長期投資"),
            new PortfolioGroup(incomeId, "現金流"),
        ]));
        var first = new PortfolioEntry(Guid.NewGuid(), "FUND-A", "", AssetType.Fund, PortfolioGroupId: longTermId);
        var second = new PortfolioEntry(Guid.NewGuid(), "BOND-B", "", AssetType.Bond, PortfolioGroupId: longTermId);
        var other = new PortfolioEntry(Guid.NewGuid(), "INCOME", "", AssetType.Fund, PortfolioGroupId: incomeId);
        var trades = new[]
        {
            new Trade(
                Guid.NewGuid(), "FUND-A", "", "Fund A", TradeType.Buy,
                new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc),
                100m, 10, RealizedPnl: null, RealizedPnlPct: null,
                PortfolioEntryId: first.Id, PortfolioGroupId: longTermId),
            new Trade(
                Guid.NewGuid(), "BOND-B", "", "Bond B", TradeType.CashDividend,
                new DateTime(2026, 5, 12, 0, 0, 0, DateTimeKind.Utc),
                Price: 0m, Quantity: 0, RealizedPnl: null, RealizedPnlPct: null,
                CashAmount: 20m, PortfolioEntryId: second.Id, PortfolioGroupId: longTermId),
            new Trade(
                Guid.NewGuid(), "INCOME", "", "Income Fund", TradeType.Buy,
                new DateTime(2026, 5, 11, 0, 0, 0, DateTimeKind.Utc),
                80m, 2, RealizedPnl: null, RealizedPnlPct: null,
                PortfolioEntryId: other.Id, PortfolioGroupId: incomeId),
        };
        var snapshots = SnapshotsFor([
            (first, 100m, 10),
            (second, 50m, 4),
            (other, 80m, 2),
        ]);
        var (vm, _) = CreateVm(
            [first, second, other],
            PositionQueryMock(snapshots).Object,
            catalog,
            trades);
        await vm.LoadAsync();
        vm.SelectedPositionRow = vm.Positions.Single(p => p.Symbol == "FUND-A");

        var commandProperty = typeof(PortfolioViewModel)
            .GetProperty("OpenSelectedPositionGroupDetailCommand");
        Assert.NotNull(commandProperty);
        var command = Assert.IsAssignableFrom<System.Windows.Input.ICommand>(
            commandProperty!.GetValue(vm));

        command.Execute(null);

        var detailProperty = typeof(PortfolioViewModel).GetProperty("SelectedPortfolioGroupDetail");
        var detail = detailProperty!.GetValue(vm);
        Assert.NotNull(detail);
        var tradesProperty = detail.GetType().GetProperty("Trades");
        Assert.NotNull(tradesProperty);
        var detailTrades = Assert.IsAssignableFrom<IReadOnlyList<TradeRowViewModel>>(
            tradesProperty!.GetValue(detail));
        Assert.Equal(["BOND-B", "FUND-A"], detailTrades.Select(t => t.Symbol));
    }

    [Fact]
    public async Task OpenSelectedPositionGroupDetail_BuildsCurrentMembershipTrend()
    {
        var longTermId = Guid.NewGuid();
        var catalog = new PortfolioGroupCatalog(new FakePortfolioGroupRepo([
            new PortfolioGroup(PortfolioGroup.DefaultId, "預設", IsSystem: true),
            new PortfolioGroup(longTermId, "長期投資"),
        ]));
        var first = new PortfolioEntry(Guid.NewGuid(), "FUND-A", "", AssetType.Fund, PortfolioGroupId: longTermId);
        var second = new PortfolioEntry(Guid.NewGuid(), "BOND-B", "", AssetType.Bond, PortfolioGroupId: longTermId);
        var snapshots = SnapshotsFor([
            (first, 10m, 10),
            (second, 20m, 5),
        ]);
        var (vm, _) = CreateVm(
            [first, second],
            PositionQueryMock(snapshots).Object,
            catalog);
        await vm.LoadAsync();
        var firstRow = vm.Positions.Single(p => p.Symbol == "FUND-A");
        var secondRow = vm.Positions.Single(p => p.Symbol == "BOND-B");
        firstRow.SparklinePoints = [10d, 12d, 14d];
        firstRow.SparklineState = 1;
        secondRow.SparklinePoints = [20d, 22d, 24d];
        secondRow.SparklineState = 1;
        vm.SelectedPositionRow = firstRow;

        var commandProperty = typeof(PortfolioViewModel)
            .GetProperty("OpenSelectedPositionGroupDetailCommand");
        Assert.NotNull(commandProperty);
        var command = Assert.IsAssignableFrom<System.Windows.Input.ICommand>(
            commandProperty!.GetValue(vm));

        command.Execute(null);

        var detailProperty = typeof(PortfolioViewModel).GetProperty("SelectedPortfolioGroupDetail");
        var detail = detailProperty!.GetValue(vm);
        Assert.NotNull(detail);
        var trendProperty = detail.GetType().GetProperty("MarketValueTrendValues");
        Assert.NotNull(trendProperty);
        var values = Assert.IsAssignableFrom<IReadOnlyList<double>>(trendProperty!.GetValue(detail));
        Assert.Equal([200d, 230d, 260d], values);
        Assert.Equal(true, detail.GetType().GetProperty("HasMarketValueTrend")?.GetValue(detail));
    }

    [Fact]
    public async Task OpenSelectedPositionGroupDetail_BuildsPerformanceTrendFromCurrentMembership()
    {
        var longTermId = Guid.NewGuid();
        var catalog = new PortfolioGroupCatalog(new FakePortfolioGroupRepo([
            new PortfolioGroup(PortfolioGroup.DefaultId, "預設", IsSystem: true),
            new PortfolioGroup(longTermId, "長期投資"),
        ]));
        var first = new PortfolioEntry(Guid.NewGuid(), "FUND-A", "", AssetType.Fund, PortfolioGroupId: longTermId);
        var second = new PortfolioEntry(Guid.NewGuid(), "BOND-B", "", AssetType.Bond, PortfolioGroupId: longTermId);
        var snapshots = SnapshotsFor([
            (first, 10m, 10),
            (second, 20m, 5),
        ]);
        var (vm, _) = CreateVm(
            [first, second],
            PositionQueryMock(snapshots).Object,
            catalog);
        await vm.LoadAsync();
        var firstRow = vm.Positions.Single(p => p.Symbol == "FUND-A");
        var secondRow = vm.Positions.Single(p => p.Symbol == "BOND-B");
        firstRow.SparklinePoints = [10d, 12d, 14d];
        firstRow.SparklineState = 1;
        secondRow.SparklinePoints = [20d, 22d, 24d];
        secondRow.SparklineState = 1;
        vm.SelectedPositionRow = firstRow;

        var commandProperty = typeof(PortfolioViewModel)
            .GetProperty("OpenSelectedPositionGroupDetailCommand");
        Assert.NotNull(commandProperty);
        var command = Assert.IsAssignableFrom<System.Windows.Input.ICommand>(
            commandProperty!.GetValue(vm));

        command.Execute(null);

        var detailProperty = typeof(PortfolioViewModel).GetProperty("SelectedPortfolioGroupDetail");
        var detail = detailProperty!.GetValue(vm);
        Assert.NotNull(detail);
        var costTrendProperty = detail.GetType().GetProperty("CostTrendValues");
        var pnlTrendProperty = detail.GetType().GetProperty("PnlTrendValues");
        var hasPerformanceTrendProperty = detail.GetType().GetProperty("HasPerformanceTrend");
        Assert.NotNull(costTrendProperty);
        Assert.NotNull(pnlTrendProperty);
        Assert.NotNull(hasPerformanceTrendProperty);
        var costTrend = Assert.IsAssignableFrom<IReadOnlyList<double>>(costTrendProperty!.GetValue(detail));
        var pnlTrend = Assert.IsAssignableFrom<IReadOnlyList<double>>(pnlTrendProperty!.GetValue(detail));
        Assert.Equal([200d, 200d, 200d], costTrend);
        Assert.Equal([0d, 30d, 60d], pnlTrend);
        Assert.Equal(true, hasPerformanceTrendProperty!.GetValue(detail));
    }

    [Fact]
    public async Task QuoteStream_UpdatesOnlyMatchingExchangePosition()
    {
        var quoteStream = new Subject<IReadOnlyList<StockQuote>>();
        var stockService = new Mock<IStockService>();
        stockService.Setup(s => s.QuoteStream).Returns(quoteStream);
        var twse = new PortfolioEntry(Guid.NewGuid(), "1234", "TWSE");
        var tpex = new PortfolioEntry(Guid.NewGuid(), "1234", "TPEX");
        var snapshots = SnapshotsFor([(twse, 10m, 100), (tpex, 20m, 100)]);
        var repo = new Mock<IPortfolioRepository>();
        repo.Setup(r => r.GetEntriesAsync()).ReturnsAsync([twse, tpex]);
        var search = new Mock<IStockSearchService>();
        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);
        var vm = new PortfolioViewModel(
            new PortfolioRepositories(repo.Object, snapshotRepo.Object, logRepo.Object, Trade: new FakeTradeRepo()),
            new PortfolioServices(stockService.Object, search.Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                PositionQuery: PositionQueryMock(snapshots).Object),
            new PortfolioUiServices(ImmediateScheduler.Instance));
        await vm.LoadAsync();

        quoteStream.OnNext(
        [
            new StockQuote(
                "1234",
                "TPEX Target",
                "TPEX",
                55m,
                1m,
                1.8m,
                1000,
                54m,
                56m,
                53m,
                54m,
                DateTimeOffset.UtcNow),
        ]);

        var twseRow = vm.Positions.Single(p => p.Symbol == "1234" && p.Exchange == "TWSE");
        var tpexRow = vm.Positions.Single(p => p.Symbol == "1234" && p.Exchange == "TPEX");
        Assert.Equal(0m, twseRow.CurrentPrice);
        Assert.Equal(55m, tpexRow.CurrentPrice);
        Assert.Equal("TPEX Target", tpexRow.Name);
    }

    // Builds a Portfolio VM wired to a settings service whose Current is mutable, so a
    // test can change settings AFTER construction and then raise Changed — mirroring what
    // AppSettingsService.SaveAsync does. Returns the VM, the recording stock service, and
    // a setter to swap Current before raising Changed.
    private (Mock<IStockService> stock, Mock<IAppSettingsService> settings, Action<AppSettings> setCurrent)
        WireSettingsChangedVm(AppSettings initial)
    {
        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync(Array.Empty<PortfolioEntry>());

        var stockService = SilentStockService();

        var current = initial;
        var settings = new Mock<IAppSettingsService>();
        settings.Setup(s => s.Current).Returns(() => current);

        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);
        _ = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object, Trade: new FakeTradeRepo()),
            new PortfolioServices(
                stockService.Object,
                new Mock<IStockSearchService>().Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill)),
            new PortfolioUiServices(ImmediateScheduler.Instance, Settings: settings.Object));

        return (stockService, settings, next => current = next);
    }

    [Fact]
    public async Task SettingsChanged_TriggersImmediateQuoteRefresh()
    {
        // WHY: changing 報價來源 / API 金鑰 must refresh prices right away. The quote
        // provider chain re-reads settings live, but the running stream only re-fetches
        // on the next ~10s poll — so the Changed handler must kick a one-shot refresh,
        // otherwise the user sees no change and assumes a restart is required.
        var refreshSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (stockService, settings, setCurrent) =
            WireSettingsChangedVm(new AppSettings(QuoteProvider: "official"));
        stockService.Setup(s => s.RefreshNowAsync(It.IsAny<CancellationToken>()))
            .Returns(() => { refreshSignal.TrySetResult(); return Task.CompletedTask; });

        // Quote-relevant change: switch provider, then fire Changed as SaveAsync would.
        setCurrent(new AppSettings(QuoteProvider: "twelvedata"));
        settings.Raise(s => s.Changed += null);

        // The refresh is fire-and-forget; wait briefly for the continuation.
        var completed = await Task.WhenAny(refreshSignal.Task, Task.Delay(2000));
        Assert.Same(refreshSignal.Task, completed);
        stockService.Verify(s => s.RefreshNowAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SettingsChanged_NoQuoteRelevantChange_DoesNotRefresh()
    {
        // WHY: settings.Changed can fire for non-quote changes (and historically for
        // internal bookkeeping persistence such as TwelveData quota usage). Re-fetching
        // on every Changed burns API quota and — when the persist itself raised Changed —
        // formed an infinite feedback loop. The handler must refresh ONLY when the quote
        // provider / API keys actually changed.
        var (stockService, settings, setCurrent) =
            WireSettingsChangedVm(new AppSettings(QuoteProvider: "twelvedata", MonthlyExpense: 1000m));

        // Change only an unrelated field; provider + keys are unchanged.
        setCurrent(new AppSettings(QuoteProvider: "twelvedata", MonthlyExpense: 2000m));
        settings.Raise(s => s.Changed += null);

        // Give any (erroneous) fire-and-forget refresh a chance to run before asserting.
        await Task.Delay(200);
        stockService.Verify(s => s.RefreshNowAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LoadAsync_EmptyRepo_HasNoPositionsIsTrue()
    {
        var (vm, _) = CreateVm([]);
        await vm.LoadAsync();
        Assert.True(vm.HasNoPositions);
        Assert.Empty(vm.Positions);
    }

    [Fact]
    public async Task LoadAsync_WithEntries_HasNoPositionsIsFalse()
    {
        var (vm, _) = CreateVm([MakeEntry()]);
        await vm.LoadAsync();
        Assert.False(vm.HasNoPositions);
    }

    // Totals

    [Fact]
    public async Task LoadAsync_RebuildsTotals_Correctly()
    {
        // cost = 100 * 1000 = 100,000
        var entry = MakeEntry("2330", price: 100m, qty: 1000);
        var snapshots = SnapshotsFor([(entry, 100m, 1000)]);
        var (vm, _) = CreateVm([entry], PositionQueryMock(snapshots).Object);
        await vm.LoadAsync();

        Assert.Equal(100_000m, vm.TotalCost);
        Assert.Equal(0m, vm.TotalMarketValue);   // no current price yet
        Assert.Equal(-100_000m, vm.TotalPnl);
        Assert.False(vm.IsTotalPositive);
    }

    [Fact]
    public async Task LoadAsync_MultiplePositions_SumsTotalCost()
    {
        var entry1 = MakeEntry("2330", price: 100m, qty: 1000);
        var entry2 = MakeEntry("2317", price: 50m, qty: 500);
        var snapshots = SnapshotsFor([(entry1, 100m, 1000), (entry2, 50m, 500)]);
        var (vm, _) = CreateVm([entry1, entry2], PositionQueryMock(snapshots).Object);
        await vm.LoadAsync();
        Assert.Equal(125_000m, vm.TotalCost);
    }

    // AddPosition

    [Fact]
    public async Task AddPosition_EmptySymbol_SetsError()
    {
        var (vm, _) = CreateVm([]);
        vm.AddAssetDialog.AddSymbol = string.Empty;
        vm.AddAssetDialog.AddPrice = "100";
        vm.AddAssetDialog.AddQuantity = "1000";

        await vm.AddAssetDialog.AddPositionCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.AddAssetDialog.AddError);
        Assert.Empty(vm.Positions);
    }

    [Fact]
    public async Task AddPosition_InvalidTotalCost_SetsError()
    {
        var (vm, _) = CreateVm([]);
        vm.AddAssetDialog.AddSymbol = "2330";
        vm.AddAssetDialog.AddPrice = "abc";
        vm.AddAssetDialog.AddQuantity = "1000";

        await vm.AddAssetDialog.AddPositionCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.AddAssetDialog.AddError);
        Assert.Empty(vm.Positions);
    }

    [Fact]
    public async Task AddPosition_ZeroPrice_SetsError()
    {
        var (vm, _) = CreateVm([]);
        vm.AddAssetDialog.AddSymbol = "2330";
        vm.AddAssetDialog.AddPrice = "0";
        vm.AddAssetDialog.AddQuantity = "1000";

        await vm.AddAssetDialog.AddPositionCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.AddAssetDialog.AddError);
    }

    [Fact]
    public async Task AddPosition_UnknownSymbol_InfersExchangeAndAdds()
    {
        var entryId1 = Guid.NewGuid();
        var created1 = new List<PortfolioEntry>();
        var repo = new Mock<IPortfolioRepository>();
        repo.Setup(r => r.GetEntriesAsync()).ReturnsAsync(() => created1.ToList());
        repo.Setup(r => r.FindOrCreatePortfolioEntryAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<AssetType>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((string sym, string exch, string? n, AssetType at, string? cur, bool etf, Guid? _, CancellationToken _) =>
                created1.Add(new PortfolioEntry(entryId1, sym, exch, at, n ?? string.Empty)))
            .ReturnsAsync(entryId1);
        repo.Setup(r => r.UnarchiveAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        var search = new Mock<IStockSearchService>();
        search.Setup(s => s.GetExchange("XXXX")).Returns((string?)null);
        var (snapshotSvc1, snapshotRepo1) = SnapshotStubs();
        var (logRepo1, backfill1) = BackfillStubs(snapshotRepo1);

        var vm = new PortfolioViewModel(
            new PortfolioRepositories(repo.Object, snapshotRepo1.Object, logRepo1.Object, Trade: new FakeTradeRepo()),
            new PortfolioServices(SilentStockService().Object, search.Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc1, backfill1)),
            new PortfolioUiServices(ImmediateScheduler.Instance));
        vm.AddAssetDialog.AddSymbol = "XXXX";
        vm.AddAssetDialog.AddPrice = "910";
        vm.AddAssetDialog.AddQuantity = "1000";

        await vm.AddAssetDialog.AddPositionCommand.ExecuteAsync(null);

        // Unknown symbols are now accepted with inferred exchange
        Assert.NotEmpty(vm.Positions);
    }

    [Fact]
    public async Task AddPosition_ValidInput_AddsToPositions()
    {
        var entryId2 = Guid.NewGuid();
        var created2 = new List<PortfolioEntry>();
        var repo = new Mock<IPortfolioRepository>();
        repo.Setup(r => r.GetEntriesAsync()).ReturnsAsync(() => created2.ToList());
        repo.Setup(r => r.FindOrCreatePortfolioEntryAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<AssetType>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback((string sym, string exch, string? n, AssetType at, string? cur, bool etf, Guid? _, CancellationToken _) =>
                created2.Add(new PortfolioEntry(entryId2, sym, exch, at, n ?? string.Empty)))
            .ReturnsAsync(entryId2);
        repo.Setup(r => r.UnarchiveAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        var search = new Mock<IStockSearchService>();
        search.Setup(s => s.GetExchange("2330")).Returns("TWSE");
        var (snapshotSvc2, snapshotRepo2) = SnapshotStubs();
        var (logRepo2, backfill2) = BackfillStubs(snapshotRepo2);

        // BuyPrice = cost per share including buy commission (discount = 1.0, no discount):
        //   gross = 910 × 1000 = 910,000; commission = floor(910000 × 0.001425 × 1.0) = floor(1296.75) = 1296
        //   costPerShare = (910000 + 1296) / 1000 = 911.296
        var posQuery = new Mock<IPositionQueryService>();
        posQuery.Setup(s => s.GetAllPositionSnapshotsAsync())
            .ReturnsAsync(() => new Dictionary<Guid, PositionSnapshot>
            {
                [entryId2] = new PositionSnapshot(entryId2, 1000m, 911_296m, 911.296m, 0m,
                    DateOnly.FromDateTime(DateTime.Today))
            });
        posQuery.Setup(s => s.ComputeRealizedPnlAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<decimal>(),
                It.IsAny<decimal>(), It.IsAny<decimal>()))
            .ReturnsAsync(0m);

        var vm = new PortfolioViewModel(
            new PortfolioRepositories(repo.Object, snapshotRepo2.Object, logRepo2.Object, Trade: new FakeTradeRepo()),
            new PortfolioServices(SilentStockService().Object, search.Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc2, backfill2),
                PositionQuery: posQuery.Object),
            new PortfolioUiServices(ImmediateScheduler.Instance));
        vm.AddAssetDialog.AddSymbol = "2330";
        vm.AddAssetDialog.AddPrice = "910";    // transaction price per share (from broker)
        vm.AddAssetDialog.AddQuantity = "1000";

        await vm.AddAssetDialog.AddPositionCommand.ExecuteAsync(null);

        Assert.Single(vm.Positions);
        Assert.Equal("2330", vm.Positions[0].Symbol);
        Assert.Equal(911.296m, vm.Positions[0].BuyPrice);
        Assert.Equal(1000, vm.Positions[0].Quantity);
        Assert.False(vm.HasNoPositions);
        // form fields should be cleared
        Assert.Empty(vm.AddAssetDialog.AddSymbol);
        Assert.Empty(vm.AddAssetDialog.AddPrice);
    }

    // ConfirmSell

    [Fact]
    public async Task ConfirmSell_ViaDialog_RemovesFromCollection()
    {
        var entry = MakeEntry("2330");
        var posQuery = new Mock<IPositionQueryService>();
        posQuery.Setup(s => s.GetAllPositionSnapshotsAsync())
            .ReturnsAsync(new Dictionary<Guid, PositionSnapshot>
            {
                [entry.Id] = new PositionSnapshot(entry.Id, 1000m, 100_000m, 100m, 0m,
                    DateOnly.FromDateTime(DateTime.Today))
            });
        posQuery.Setup(s => s.ComputeRealizedPnlAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<decimal>(),
                It.IsAny<decimal>(), It.IsAny<decimal>()))
            .ReturnsAsync(0m);

        var (vm, _) = CreateVm([entry], posQuery.Object);
        await vm.LoadAsync();
        Assert.Single(vm.Positions);

        var row = vm.Positions[0];
        // BeginSell opens TxDialog in Sell mode with position pre-selected
        vm.BeginSellCommand.Execute(row);
        Assert.True(vm.Transaction.IsTxDialogOpen);
        Assert.Equal("sell", vm.Transaction.TxType);

        // Set sell price via TxAmount (the Sell form binds to TxAmount)
        vm.Transaction.TxAmount = "1000";
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Empty(vm.Positions);
        Assert.True(vm.HasNoPositions);
    }

    // PortfolioRowViewModel.Refresh

    [Fact]
    public void RowRefresh_CalculatesPnlCorrectly()
    {
        var row = new PortfolioRowViewModel
        {
            Id = Guid.NewGuid(),
            Symbol = "2330",
            Quantity = 1000,
            BuyPrice = 900m,
        };
        row.CurrentPrice = 950m;
        row.Refresh();

        Assert.Equal(900_000m, row.Cost);
        Assert.Equal(950_000m, row.MarketValue);
        Assert.Equal(4_203m, row.EstimatedSellFee);
        Assert.Equal(945_797m, row.NetValue);
        Assert.Equal(45_797m, row.Pnl);
        Assert.True(row.IsPnlPositive);
    }

    [Fact]
    public void RowRefresh_NegativePnl_IsPnlPositiveIsFalse()
    {
        var row = new PortfolioRowViewModel
        {
            Id = Guid.NewGuid(),
            Symbol = "2317",
            Quantity = 1000,
            BuyPrice = 100m,
        };
        row.CurrentPrice = 80m;
        row.Refresh();

        Assert.Equal(354m, row.EstimatedSellFee);
        Assert.Equal(-20_354m, row.Pnl);
        Assert.False(row.IsPnlPositive);
    }


    // ── Tab navigation regression tests ─────────────────────────────────────
    // Verify that tab commands always leave exactly one tab active and that
    // programmatic navigation (from dashboard / add-liability shortcuts) sets
    // the expected tab, not "all inactive".

    [Fact]
    public void SelectedTab_DefaultsToPositions()
    {
        var (vm, _) = CreateVm();
        Assert.Equal(PortfolioTab.Positions, vm.SelectedTab);
    }

    [Fact]
    public void OpenAddAccountDialog_SwitchesToAccountsTab()
    {
        var (vm, _) = CreateVm();
        vm.OpenAddAccountDialogCommand.Execute(null);
        Assert.Equal(PortfolioTab.Accounts, vm.SelectedTab);
    }

    [Fact]
    public void OpenAddLiabilityDialog_SwitchesToLiabilityTab()
    {
        var (vm, _) = CreateVm();
        vm.OpenAddLiabilityDialogCommand.Execute(null);
        Assert.Equal(PortfolioTab.Liability, vm.SelectedTab);
    }

    [Fact]
    public void SelectedTab_CanBeSetToEveryValidTab()
    {
        var (vm, _) = CreateVm();
        foreach (var tab in Enum.GetValues<PortfolioTab>())
        {
            vm.SelectedTab = tab;
            Assert.Equal(tab, vm.SelectedTab);
        }
    }

    [Fact]
    public void OpenAddAccountDialog_ThenOpenAddLiability_LeavesLiabilityTabActive()
    {
        var (vm, _) = CreateVm();
        vm.OpenAddAccountDialogCommand.Execute(null);
        vm.OpenAddLiabilityDialogCommand.Execute(null);
        Assert.Equal(PortfolioTab.Liability, vm.SelectedTab);
    }

    // Liability ⇄ 借款/還款 balance adjustment
    //
    // These tests reproduce the user report:
    //   "在交易記錄頁面按新增，選擇借款/還款並指定負債帳戶，負債餘額看起來沒有累加
    //    (或還款反而增加)"
    // The flow is: open Tx dialog → pick loanBorrow / loanRepay → choose liability →
    // enter amount → Confirm. Balance should increase for borrow, decrease for repay.

    private sealed class FakeAssetRepo : IAssetRepository
    {
        public List<AssetItem> Store { get; } = new();

        public Task<IReadOnlyList<AssetItem>> GetItemsAsync() =>
            Task.FromResult<IReadOnlyList<AssetItem>>(Store.ToList());
        public Task<IReadOnlyList<AssetItem>> GetItemsByTypeAsync(FinancialType type) =>
            Task.FromResult<IReadOnlyList<AssetItem>>(Store.Where(a => a.Type == type).ToList());
        public Task<AssetItem?> GetByIdAsync(Guid id) =>
            Task.FromResult(Store.FirstOrDefault(a => a.Id == id));
        public Task AddItemAsync(AssetItem item) { Store.Add(item); return Task.CompletedTask; }
        public Task UpdateItemAsync(AssetItem item)
        {
            var i = Store.FindIndex(a => a.Id == item.Id);
            if (i >= 0)
                Store[i] = item;
            return Task.CompletedTask;
        }
        public Task DeleteItemAsync(Guid id) { Store.RemoveAll(a => a.Id == id); return Task.CompletedTask; }
        public Task<Guid> FindOrCreateAccountAsync(string name, string currency, CancellationToken ct = default) =>
            Task.FromResult(Guid.NewGuid());
        public Task ArchiveItemAsync(Guid id) => Task.CompletedTask;
        public Task UnarchiveItemAsync(Guid id) => Task.CompletedTask;
        public Task<int> HasTradeReferencesAsync(Guid id, CancellationToken ct = default) => Task.FromResult(0);

        public Task<IReadOnlyList<AssetGroup>> GetGroupsAsync() =>
            Task.FromResult<IReadOnlyList<AssetGroup>>([]);
        public Task AddGroupAsync(AssetGroup group) => Task.CompletedTask;
        public Task UpdateGroupAsync(AssetGroup group) => Task.CompletedTask;
        public Task DeleteGroupAsync(Guid id) => Task.CompletedTask;

        public Task<IReadOnlyList<AssetEvent>> GetEventsAsync(Guid assetId) =>
            Task.FromResult<IReadOnlyList<AssetEvent>>([]);
        public Task AddEventAsync(AssetEvent evt) => Task.CompletedTask;
        public Task DeleteEventAsync(Guid id) => Task.CompletedTask;
        public Task<AssetEvent?> GetLatestValuationAsync(Guid assetId) =>
            Task.FromResult<AssetEvent?>(null);
    }

    private sealed class FakeTradeRepo : ITradeRepository
    {
        public List<Trade> Store { get; } = new();
        public Task<IReadOnlyList<Trade>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Trade>>(Store.ToList());
        public Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid cashAccountId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Trade>>(
                Store.Where(t => t.CashAccountId == cashAccountId
                              || t.ToCashAccountId == cashAccountId).ToList());
        public Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string loanLabel, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Trade>>(
                Store.Where(t => t.LoanLabel == loanLabel).ToList());
        public Task<Trade?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<Trade?>(Store.FirstOrDefault(t => t.Id == id));
        public Task AddAsync(Trade t, CancellationToken ct = default) { Store.Add(t); return Task.CompletedTask; }
        public Task UpdateAsync(Trade t, CancellationToken ct = default)
        {
            var i = Store.FindIndex(x => x.Id == t.Id);
            if (i >= 0)
                Store[i] = t;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(Guid id, CancellationToken ct = default) { Store.RemoveAll(x => x.Id == id); return Task.CompletedTask; }
        public Task RemoveChildrenAsync(Guid parentId, CancellationToken ct = default) { Store.RemoveAll(x => x.ParentTradeId == parentId); return Task.CompletedTask; }
        public Task RemoveByAccountIdAsync(Guid accountId, CancellationToken ct = default) { Store.RemoveAll(x => x.CashAccountId == accountId || x.ToCashAccountId == accountId); return Task.CompletedTask; }
        public Task RemoveByLiabilityAsync(Guid? liabilityAssetId, string? loanLabel, CancellationToken ct = default)
        {
            Store.RemoveAll(x =>
                (liabilityAssetId.HasValue && x.LiabilityAssetId == liabilityAssetId.Value) ||
                (!string.IsNullOrEmpty(loanLabel) && x.LoanLabel == loanLabel));
            return Task.CompletedTask;
        }
        public Task ApplyAtomicAsync(IReadOnlyList<TradeMutation> mutations, CancellationToken ct = default)
        {
            foreach (var m in mutations)
            {
                switch (m)
                {
                    case AddTradeMutation add:
                        Store.Add(add.Trade);
                        break;
                    case RemoveTradeMutation rem:
                        Store.RemoveAll(t => t.Id == rem.Id);
                        break;
                }
            }
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Single-truth helper: creates a VM wired with an empty <see cref="FakeAssetRepo"/>
    /// plus a <see cref="FakeTradeRepo"/> pre-seeded so projection yields the requested
    /// <paramref name="initialBalance"/>/<paramref name="original"/> snapshot.
    /// The 4th element is the loan label string used in the seeded trades.
    /// </summary>
    private static async Task<(PortfolioViewModel vm, FakeAssetRepo assetRepo, FakeTradeRepo tradeRepo, string loanLabel)>
        CreateVmWithLiabilityAsync(decimal initialBalance, decimal original)
    {
        const string loanLabel = "台新 7y";

        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync([]);

        var assetRepo = new FakeAssetRepo();
        var tradeRepo = new FakeTradeRepo();

        // Seed trades so projection produces (Balance=initialBalance, OriginalAmount=original).
        await SeedLiabilityBaselineAsync(tradeRepo, loanLabel, initialBalance, original);

        var search = new Mock<IStockSearchService>();
        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);
        var txService = new TransactionService(tradeRepo);
        var balanceQuery = new BalanceQueryService(tradeRepo);

        var vm = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object, Trade: tradeRepo, Asset: assetRepo, LoanSchedule: new Mock<ILoanScheduleRepository>().Object),
            new PortfolioServices(SilentStockService().Object, search.Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                TransactionWorkflow: new TransactionWorkflowService(txService),
                BalanceQuery: balanceQuery),
            new PortfolioUiServices(ImmediateScheduler.Instance));
        await vm.LoadAsync();
        return (vm, assetRepo, tradeRepo, loanLabel);
    }

    private static async Task SeedLiabilityBaselineAsync(
        FakeTradeRepo tradeRepo, string liabilityName,
        decimal initialBalance, decimal original)
    {
        if (original > 0m)
        {
            await tradeRepo.AddAsync(new Trade(
                Id: Guid.NewGuid(), Symbol: liabilityName, Exchange: string.Empty,
                Name: liabilityName, Type: TradeType.LoanBorrow,
                TradeDate: DateTime.UtcNow.AddDays(-1),
                Price: 0, Quantity: 1, RealizedPnl: null, RealizedPnlPct: null,
                CashAmount: original, LoanLabel: liabilityName, Note: "seed"));
        }
        if (original > initialBalance)
        {
            var principalRepaid = original - initialBalance;
            await tradeRepo.AddAsync(new Trade(
                Id: Guid.NewGuid(), Symbol: liabilityName, Exchange: string.Empty,
                Name: liabilityName, Type: TradeType.LoanRepay,
                TradeDate: DateTime.UtcNow.AddDays(-1),
                Price: 0, Quantity: 1, RealizedPnl: null, RealizedPnlPct: null,
                CashAmount: principalRepaid, LoanLabel: liabilityName,
                Principal: principalRepaid, Note: "seed"));
        }
    }

    [Fact]
    public async Task BeginTxForSelectedLiability_PrefillsSelectedLiabilityInTransactionDialog()
    {
        var (vm, _, _, _) = await CreateVmWithLiabilityAsync(
            initialBalance: 2_000_000m, original: 2_000_000m);
        var liability = vm.Liabilities.First();

        vm.SelectedLiabilityRow = liability;
        vm.BeginTxForSelectedLiabilityCommand.Execute("loanRepay");

        Assert.True(vm.Transaction.IsTxDialogOpen);
        Assert.True(vm.Transaction.IsAssetContextLocked);
        Assert.False(vm.Transaction.ShowAssetSelector);
        Assert.Equal("loanRepay", vm.Transaction.TxType);
        Assert.NotNull(vm.Transaction.SelectedAsset);
        Assert.Equal(TxAssetKind.Liability, vm.Transaction.SelectedAsset.Kind);
        Assert.Equal(liability.Label, vm.Transaction.SelectedAsset.PrimaryName);
        Assert.Equal(liability.Label, vm.Transaction.Loan.Label);
    }

    [Fact]
    public async Task ConfirmTx_LoanBorrow_IncreasesLiabilityBalance()
    {
        var (vm, liabRepo, _, _) = await CreateVmWithLiabilityAsync(
            initialBalance: 2_000_000m, original: 2_000_000m);

        vm.Transaction.TxType = "loanBorrow";
        vm.Transaction.TxAmount = "50000";
        vm.Transaction.Loan.Label = vm.Liabilities.First().Label;
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Equal(2_050_000m, vm.Liabilities[0].Balance);
    }

    [Fact]
    public async Task ConfirmTx_LoanRepay_DecreasesLiabilityBalance()
    {
        var (vm, _, _, _) = await CreateVmWithLiabilityAsync(
            initialBalance: 2_000_000m, original: 2_000_000m);

        vm.Transaction.TxType = "loanRepay";
        vm.Transaction.Loan.Principal = "25979";   // Phase 3: LoanRepay uses Principal/InterestPaid split
        vm.Transaction.Loan.Label = vm.Liabilities.First().Label;
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Equal(1_974_021m, vm.Liabilities[0].Balance);
    }

    [Fact]
    public async Task AddCreditCard_ValidInput_AddsLiabilityRow()
    {
        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync([]);
        var assetRepo = new FakeAssetRepo();
        var tradeRepo = new FakeTradeRepo();
        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);
        var txService = new TransactionService(tradeRepo);
        var balanceQuery = new BalanceQueryService(tradeRepo);

        var vm = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object, Trade: tradeRepo, Asset: assetRepo),
            new PortfolioServices(
                SilentStockService().Object,
                new Mock<IStockSearchService>().Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                TransactionWorkflow: new TransactionWorkflowService(txService),
                CreditCardMutation: new CreditCardMutationWorkflowService(assetRepo),
                BalanceQuery: balanceQuery),
            new PortfolioUiServices(ImmediateScheduler.Instance));
        await vm.LoadAsync();

        vm.OpenAddLiabilityDialogCommand.Execute(null);
        vm.AddAssetDialog.AddAssetType = "creditCard";
        vm.AddAssetDialog.AddCreditCardName = "富邦 J 卡";
        vm.AddAssetDialog.AddCreditCardIssuer = "Fubon";
        vm.AddAssetDialog.AddCreditCardBillingDay = "5";
        vm.AddAssetDialog.AddCreditCardDueDay = "20";
        vm.AddAssetDialog.AddCreditCardLimit = "80000";

        await vm.AddAssetDialog.ConfirmAddCommand.ExecuteAsync(null);

        Assert.Single(vm.Liabilities);
        Assert.Equal("富邦 J 卡", vm.Liabilities[0].Label);
        Assert.True(vm.Liabilities[0].IsCreditCard);
        Assert.Equal(0m, vm.Liabilities[0].Balance);
    }

    [Fact]
    public async Task ConfirmTx_CreditCardCharge_IncreasesCardBalance()
    {
        var (vm, _, _, card) = await CreateVmWithCreditCardAndCashAsync(initialCardBalance: 0m, initialCash: 30_000m);

        vm.Transaction.TxType = "creditCardCharge";
        vm.Transaction.CreditCard.Card = vm.Liabilities.Single(l => l.AssetId == card.Id);
        vm.Transaction.TxAmount = "3500";
        vm.Transaction.TxNote = "超商";
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        var row = vm.Liabilities.Single(l => l.AssetId == card.Id);
        Assert.Equal(3_500m, row.Balance);
        Assert.Equal(3_500m, row.OriginalAmount);
        Assert.Contains(vm.Trades, t => t.Type == TradeType.CreditCardCharge && t.LiabilityAssetId == card.Id);
    }

    [Fact]
    public async Task ConfirmTx_CreditCardPayment_DecreasesCardBalanceAndCash()
    {
        var (vm, assetRepo, _, card) = await CreateVmWithCreditCardAndCashAsync(initialCardBalance: 12_000m, initialCash: 50_000m);
        var cashId = assetRepo.Store.First(a => a.Type == FinancialType.Asset).Id;

        vm.Transaction.TxType = "creditCardPayment";
        vm.Transaction.CreditCard.Card = vm.Liabilities.Single(l => l.AssetId == card.Id);
        vm.Transaction.TxCashAccount = vm.CashAccounts.Single(a => a.Id == cashId);
        vm.Transaction.TxAmount = "4000";
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        var cardRow = vm.Liabilities.Single(l => l.AssetId == card.Id);
        Assert.Equal(8_000m, cardRow.Balance);
        Assert.Equal(12_000m, cardRow.OriginalAmount);
        Assert.Equal(46_000m, vm.CashAccounts.Single(a => a.Id == cashId).Balance);
    }

    /// <summary>
    /// Regression (user report 2026-05): once a payment fully clears a card, the card's
    /// Balance is 0. Editing that payment — even just its date — must NOT resurface the
    /// "this card has no outstanding balance" guard, which is only meaningful for a
    /// brand-new payment. The bug: <c>ConfirmTx</c> nulls <c>EditingTradeId</c> before
    /// dispatching (it promotes the edit to an implicit revision), so the guard's old
    /// <c>!IsEditMode</c> condition was always true and fired against the now-zero balance.
    /// The guard must key off <c>!IsRevisionMode</c> instead — the edit signal that
    /// actually survives the dispatch. This test drives the real edit→implicit-revision
    /// path and would fail on the pre-fix code (TxError = "...沒有未繳金額...").
    /// </summary>
    [Fact]
    public async Task EditTrade_CreditCardPayment_DateOnlyChange_OnPaidOffCard_DoesNotTriggerNoBalanceGuard()
    {
        var (vm, assetRepo, _, card) = await CreateVmWithCreditCardAndCashAsync(
            initialCardBalance: 4_000m, initialCash: 50_000m);
        var cashId = assetRepo.Store.First(a => a.Type == FinancialType.Asset).Id;

        // Pay the card off in full → Balance becomes 0.
        vm.Transaction.TxType = "creditCardPayment";
        vm.Transaction.CreditCard.Card = vm.Liabilities.Single(l => l.AssetId == card.Id);
        vm.Transaction.TxCashAccount = vm.CashAccounts.Single(a => a.Id == cashId);
        vm.Transaction.TxAmount = "4000";
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);
        Assert.Equal(0m, vm.Liabilities.Single(l => l.AssetId == card.Id).Balance);

        // Edit that payment and change ONLY the date (the user's exact action).
        var paymentRow = vm.Trades.Single(t => t.Type == TradeType.CreditCardPayment);
        vm.Transaction.EditTradeCommand.Execute(paymentRow);
        vm.Transaction.TxDate = vm.Transaction.TxDate.AddDays(-3);
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        // Edit must succeed silently: no false "no balance" warning, card still paid off,
        // and exactly one payment trade remains (the original replaced by the revision).
        Assert.True(string.IsNullOrEmpty(vm.Transaction.TxError),
            $"Editing a paid-off card's payment must not trigger the new-payment balance guard, but TxError was: {vm.Transaction.TxError}");
        Assert.Equal(0m, vm.Liabilities.Single(l => l.AssetId == card.Id).Balance);
        Assert.Single(vm.Trades.Where(t => t.Type == TradeType.CreditCardPayment));
    }

    [Fact]
    public async Task ConfirmTx_LoanRepay_PreservesOriginalAmount()
    {
        // Regression: LoanRepay must not shrink the original-borrow total, otherwise the
        // PaidPercent progress bar resets (OriginalAmount is Σ LoanBorrow, not the
        // residual balance).
        var (vm, _, _, _) = await CreateVmWithLiabilityAsync(
            initialBalance: 2_000_000m, original: 2_000_000m);

        vm.Transaction.TxType = "loanRepay";
        vm.Transaction.Loan.Principal = "25979";
        vm.Transaction.Loan.Label = vm.Liabilities.First().Label;
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Equal(2_000_000m, vm.Liabilities[0].OriginalAmount);
    }

    [Fact]
    public async Task RemoveTrade_LoanRepay_RestoresLiabilityBalance()
    {
        // Regression: deleting a 還款 trade should add the amount back to Balance.
        // Symmetric: deleting 借款 should subtract.
        var (vm, liabRepo, _, _) = await CreateVmWithLiabilityAsync(
            initialBalance: 2_000_000m, original: 2_000_000m);

        vm.Transaction.TxType = "loanRepay";
        vm.Transaction.Loan.Principal = "25979";
        vm.Transaction.Loan.Label = vm.Liabilities.First().Label;
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);
        Assert.Equal(1_974_021m, vm.Liabilities[0].Balance);

        // Delete the trade — RemoveTrade opens a confirm dialog; trigger "Yes" to proceed
        var trade = vm.Trades.First(t => t.IsLoanRepay);
        vm.RemoveTradeCommand.Execute(trade);
        await vm.ConfirmDialogYesCommand.ExecuteAsync(null);

        // Balance should be restored to the pre-trade value
        Assert.Equal(2_000_000m, vm.Liabilities[0].Balance);
    }

    [Fact]
    public async Task RemoveTrade_LoanRepay_ReloadsOpenLoanSchedule()
    {
        // Regression: the open liability detail panel must treat trades as the source of
        // truth. Deleting a repayment trade should immediately unlock the linked schedule row.
        var loanAssetId = Guid.NewGuid();
        const string loanLabel = "台新 7y A";
        var repayTradeId = Guid.NewGuid();
        var scheduleEntryId = Guid.NewGuid();
        var linkedTradeStillExists = true;

        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync([]);

        var assetRepo = new FakeAssetRepo();
        assetRepo.Store.Add(new AssetItem(
            loanAssetId,
            loanLabel,
            FinancialType.Liability,
            GroupId: null,
            Currency: "TWD",
            CreatedDate: new DateOnly(2025, 1, 1),
            LoanAnnualRate: 0.025m,
            LoanTermMonths: 84,
            LoanStartDate: new DateOnly(2025, 1, 1)));

        var tradeRepo = new FakeTradeRepo();
        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: loanLabel, Exchange: string.Empty,
            Name: loanLabel, Type: TradeType.LoanBorrow,
            TradeDate: new DateTime(2025, 1, 1),
            Price: 0, Quantity: 1, RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 2_000_000m, LoanLabel: loanLabel, LiabilityAssetId: loanAssetId));
        await tradeRepo.AddAsync(new Trade(
            Id: repayTradeId, Symbol: loanLabel, Exchange: string.Empty,
            Name: loanLabel, Type: TradeType.LoanRepay,
            TradeDate: new DateTime(2026, 5, 30),
            Price: 0, Quantity: 1, RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 25_978m, LoanLabel: loanLabel, LiabilityAssetId: loanAssetId,
            Principal: 22_833m, InterestPaid: 3_145m));

        var scheduleRepo = new Mock<ILoanScheduleRepository>();
        scheduleRepo.Setup(r => r.ClearPaidWithoutActiveTradeAsync(loanAssetId))
            .Returns(Task.CompletedTask);
        scheduleRepo.Setup(r => r.ReconcilePaidFromActiveRepaymentsAsync(loanAssetId))
            .Returns(Task.CompletedTask);
        scheduleRepo.Setup(r => r.ClearPaidByTradeIdAsync(It.IsAny<Guid>()))
            .Callback<Guid>(id =>
            {
                if (id == repayTradeId)
                    linkedTradeStillExists = false;
            })
            .Returns(Task.CompletedTask);
        scheduleRepo.Setup(r => r.GetByAssetAsync(loanAssetId))
            .ReturnsAsync(() =>
            {
                var paid = linkedTradeStillExists;
                return
                [
                    new LoanScheduleEntry(
                        scheduleEntryId, loanAssetId, 23, new DateOnly(2026, 5, 30),
                        25_978m, 22_833m, 3_145m, 1_900_000m,
                        paid, paid ? new DateTime(2026, 5, 30) : null,
                        paid ? repayTradeId : null)
                ];
            });

        var search = new Mock<IStockSearchService>();
        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);
        var txService = new TransactionService(tradeRepo);
        var balanceQuery = new BalanceQueryService(tradeRepo);
        var positionQuery = PositionQueryMock(new Dictionary<Guid, PositionSnapshot>());

        var vm = new PortfolioViewModel(
            new PortfolioRepositories(
                portfolioRepo.Object, snapshotRepo.Object, logRepo.Object,
                Trade: tradeRepo, Asset: assetRepo, LoanSchedule: scheduleRepo.Object),
            new PortfolioServices(
                SilentStockService().Object,
                search.Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                TransactionWorkflow: new TransactionWorkflowService(txService),
                BalanceQuery: balanceQuery,
                PositionQuery: positionQuery.Object,
                LoanSchedule: new LoanScheduleService(scheduleRepo.Object),
                TradeDeletionWorkflow: new TradeDeletionWorkflowService(
                    tradeRepo,
                    portfolioRepo.Object,
                    positionQuery.Object,
                    loanScheduleRepository: scheduleRepo.Object)),
            new PortfolioUiServices(ImmediateScheduler.Instance));

        await vm.LoadAsync();
        var row = vm.Liabilities.Single(l => l.AssetId == loanAssetId);
        vm.SelectedLiabilityRow = row;
        await vm.Loan.LoadLoanScheduleAsync(row);
        Assert.True(row.ScheduleEntries.Single().IsPaid);

        var trade = vm.Trades.Single(t => t.Id == repayTradeId);
        vm.RemoveTradeCommand.Execute(trade);
        await vm.ConfirmDialogYesCommand.ExecuteAsync(null);

        var refreshed = vm.Liabilities.Single(l => l.AssetId == loanAssetId);
        Assert.Same(refreshed, vm.SelectedLiabilityRow);
        Assert.True(refreshed.IsScheduleLoaded);
        Assert.False(refreshed.ScheduleEntries.Single().IsPaid);
    }

    [Fact]
    public async Task ConfirmTx_LoanBorrow_AlsoGrowsOriginalAmount()
    {
        // Regression for the 3,988,000 vs 1,994,000 report: LoanBorrow must grow OriginalAmount
        // alongside Balance so PaidPercent = (Original - Balance)/Original stays ≤ 100 %.
        var (vm, _, _, _) = await CreateVmWithLiabilityAsync(
            initialBalance: 0m, original: 0m);

        vm.Transaction.TxType = "loanBorrow";
        vm.Transaction.TxAmount = "1994000";
        vm.Transaction.Loan.Label = "台新 7y";
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        var row = vm.Liabilities[0];
        Assert.Equal(1_994_000m, row.Balance);
        Assert.Equal(1_994_000m, row.OriginalAmount);
        Assert.InRange(row.PaidPercent, 0.0, 0.01); // 0 % paid, nothing odd
    }

    [Fact]
    public async Task ConfirmTx_LoanRepay_DoesNotShrinkOriginalAmount()
    {
        // OriginalAmount represents cumulative borrowed; repayment must not reduce it.
        var (vm, _, _, _) = await CreateVmWithLiabilityAsync(
            initialBalance: 2_000_000m, original: 2_000_000m);

        vm.Transaction.TxType = "loanRepay";
        vm.Transaction.Loan.Principal = "500000";
        vm.Transaction.Loan.Label = vm.Liabilities.First().Label;
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Equal(1_500_000m, vm.Liabilities[0].Balance);
        Assert.Equal(2_000_000m, vm.Liabilities[0].OriginalAmount);
        Assert.Equal(25.0, vm.Liabilities[0].PaidPercent); // (2M-1.5M)/2M = 25 %
    }

    [Fact]
    public async Task EditTrade_LoanRepay_ChangingPrincipalAppliesViaImplicitRevision()
    {
        // Source-of-truth pass (commit 6836323): LoanRepay is non-meta-only, so editing the
        // principal in the dialog promotes ConfirmTx to implicit revision. The projection
        // re-derives the liability balance from the new principal (delete-old + create-new):
        //   2_000_000 (original) - 50_000 (revised principal) = 1_950_000.
        var (vm, liabRepo, _, _) = await CreateVmWithLiabilityAsync(
            initialBalance: 2_000_000m, original: 2_000_000m);

        vm.Transaction.TxType = "loanRepay";
        vm.Transaction.Loan.Principal = "25979";
        vm.Transaction.Loan.Label = vm.Liabilities.First().Label;
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);
        Assert.Equal(1_974_021m, vm.Liabilities[0].Balance);

        // Open edit mode and change the principal — applies via implicit revision.
        var trade = vm.Trades.First(t => t.IsLoanRepay);
        vm.Transaction.EditTradeCommand.Execute(trade);
        Assert.True(vm.Transaction.AreEconomicFieldsEditable);
        Assert.False(vm.Transaction.IsEditingMetaOnly);
        vm.Transaction.Loan.Principal = "50000";
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Equal(1_950_000m, vm.Liabilities[0].Balance);
    }

    // Add liability (simplified): name only, zero balance

    [Fact]
    public async Task LoanBorrow_CreatesLiabilityRow_ViaProjection()
    {
        // Under the new model, liabilities are created implicitly by recording a LoanBorrow
        // trade — no pre-creation dialog needed. The projection produces the row.
        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync([]);
        var assetRepo = new FakeAssetRepo();
        var tradeRepo = new FakeTradeRepo();
        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);
        var txService = new TransactionService(tradeRepo);
        var balanceQuery = new BalanceQueryService(tradeRepo);

        var vm = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object, Trade: tradeRepo, Asset: assetRepo, LoanSchedule: new Mock<ILoanScheduleRepository>().Object),
            new PortfolioServices(SilentStockService().Object, new Mock<IStockSearchService>().Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                TransactionWorkflow: new TransactionWorkflowService(txService),
                BalanceQuery: balanceQuery),
            new PortfolioUiServices(ImmediateScheduler.Instance));
        await vm.LoadAsync();

        // No liabilities before any loan trade
        Assert.Empty(vm.Liabilities);

        // Record a LoanBorrow — this implicitly creates the liability row
        vm.Transaction.TxType = "loanBorrow";
        vm.Transaction.TxAmount = "1000000";
        vm.Transaction.Loan.Label = "台新 7y";
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Single(vm.Liabilities);
        Assert.Equal("台新 7y", vm.Liabilities[0].Label);
        Assert.Equal(1_000_000m, vm.Liabilities[0].Balance);
        Assert.Equal(1_000_000m, vm.Liabilities[0].OriginalAmount);
    }

    [Fact]
    public async Task ConfirmTx_LoanBorrow_EmptyLabel_SetsError()
    {
        var (vm, _, _, _) = await CreateVmWithLiabilityAsync(0m, 0m);
        vm.Transaction.TxType = "loanBorrow";
        vm.Transaction.TxAmount = "1000000";
        vm.Transaction.Loan.Label = "   ";
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.Transaction.TxError);
        Assert.Contains("貸款名稱", vm.Transaction.TxError);
    }

    // Cash account: simplified add + tx-dialog empty-state

    /// <summary>
    /// Single-truth helper: creates a VM wired with a <see cref="FakeAssetRepo"/> that has
    /// one cash account and a pre-seeded Deposit trade so projection yields
    /// <paramref name="initialBalance"/>. When <paramref name="initialBalance"/> is 0 the
    /// store is empty (both the cash account and any seed trade are skipped) so tests can
    /// exercise empty-state paths.
    /// </summary>
    private static async Task<(PortfolioViewModel vm, FakeAssetRepo cashRepo, FakeTradeRepo tradeRepo)>
        CreateVmWithCashAsync(decimal initialBalance)
    {
        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync([]);

        var assetRepo = new FakeAssetRepo();
        var tradeRepo = new FakeTradeRepo();
        if (initialBalance != 0m)
        {
            var account = new AssetItem(
                Guid.NewGuid(), "永豐 USD", FinancialType.Asset, null, "TWD",
                DateOnly.FromDateTime(DateTime.Today));
            assetRepo.Store.Add(account);
            await SeedCashBaselineAsync(tradeRepo, account.Id, account.Name, initialBalance);
        }

        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);
        var txService = new TransactionService(tradeRepo);
        var balanceQuery = new BalanceQueryService(tradeRepo);

        var vm = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object, Trade: tradeRepo, Asset: assetRepo),
            new PortfolioServices(SilentStockService().Object, new Mock<IStockSearchService>().Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                TransactionWorkflow: new TransactionWorkflowService(txService),
                BalanceQuery: balanceQuery),
            new PortfolioUiServices(ImmediateScheduler.Instance));
        await vm.LoadAsync();
        return (vm, assetRepo, tradeRepo);
    }

    private static async Task SeedCashBaselineAsync(
        FakeTradeRepo tradeRepo, Guid cashId, string cashName, decimal initialBalance)
    {
        if (initialBalance > 0m)
        {
            await tradeRepo.AddAsync(new Trade(
                Id: Guid.NewGuid(), Symbol: cashName, Exchange: string.Empty,
                Name: cashName, Type: TradeType.Deposit,
                TradeDate: DateTime.UtcNow.AddDays(-1),
                Price: 0, Quantity: 1, RealizedPnl: null, RealizedPnlPct: null,
                CashAmount: initialBalance, CashAccountId: cashId, Note: "seed"));
        }
        else if (initialBalance < 0m)
        {
            await tradeRepo.AddAsync(new Trade(
                Id: Guid.NewGuid(), Symbol: cashName, Exchange: string.Empty,
                Name: cashName, Type: TradeType.Withdrawal,
                TradeDate: DateTime.UtcNow.AddDays(-1),
                Price: 0, Quantity: 1, RealizedPnl: null, RealizedPnlPct: null,
                CashAmount: -initialBalance, CashAccountId: cashId, Note: "seed"));
        }
    }

    private static async Task SeedCreditCardBaselineAsync(
        FakeTradeRepo tradeRepo, AssetItem card, decimal initialBalance)
    {
        if (initialBalance <= 0m)
            return;

        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: string.Empty, Exchange: string.Empty,
            Name: card.Name, Type: TradeType.CreditCardCharge,
            TradeDate: DateTime.UtcNow.AddDays(-1),
            Price: 0, Quantity: 1, RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: initialBalance, LiabilityAssetId: card.Id, Note: "seed"));
    }

    [Fact]
    public async Task AddCash_NameOnly_CreatesAccountWithZeroBalance()
    {
        var (vm, cashRepo, _) = await CreateVmWithCashAsync(0m);

        vm.AddAssetDialog.AddAssetType = "cash";
        vm.AddAssetDialog.AddAccountName = "永豐 USD";
        await vm.AddAssetDialog.ConfirmAddCommand.ExecuteAsync(null);

        Assert.Single(cashRepo.Store);
        Assert.Equal("永豐 USD", cashRepo.Store[0].Name);
        Assert.Single(vm.CashAccounts);
        Assert.Equal(0m, vm.CashAccounts[0].Balance);
    }

    [Fact]
    public async Task ConfirmTx_Deposit_NoCashAccounts_ShowsHelpfulError()
    {
        // User tries to record a deposit before creating any cash account → error
        // should point them to the Cash tab, not just "請選擇現金帳戶" (empty dropdown).
        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync([]);
        var assetRepo = new FakeAssetRepo(); // empty store
        var tradeRepo = new FakeTradeRepo();
        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);
        var vm = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object, Trade: tradeRepo, Asset: assetRepo),
            new PortfolioServices(SilentStockService().Object, new Mock<IStockSearchService>().Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill)),
            new PortfolioUiServices(ImmediateScheduler.Instance));
        await vm.LoadAsync();

        vm.Transaction.TxType = "deposit";
        vm.Transaction.TxAmount = "5000";
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Contains("帳戶", vm.Transaction.TxError);
        Assert.Contains("建立", vm.Transaction.TxError);
        Assert.Empty(tradeRepo.Store); // nothing was written
    }

    [Fact]
    public async Task ConfirmTx_LoanBorrow_EmptyLoanLabel_ShowsError()
    {
        // Under the new model, liabilities are created by recording a LoanBorrow.
        // If the loan label is empty, the user gets a helpful error.
        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync([]);
        var assetRepo = new FakeAssetRepo();
        var tradeRepo = new FakeTradeRepo();
        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);
        var txService = new TransactionService(tradeRepo);
        var balanceQuery = new BalanceQueryService(tradeRepo);
        var vm = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object, Trade: tradeRepo, Asset: assetRepo),
            new PortfolioServices(SilentStockService().Object, new Mock<IStockSearchService>().Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                TransactionWorkflow: new TransactionWorkflowService(txService),
                BalanceQuery: balanceQuery),
            new PortfolioUiServices(ImmediateScheduler.Instance));
        await vm.LoadAsync();

        vm.Transaction.TxType = "loanBorrow";
        vm.Transaction.TxAmount = "1000000";
        // Loan.Label is empty — should show an error
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Contains("貸款名稱", vm.Transaction.TxError);
        Assert.Empty(tradeRepo.Store);
    }

    [Fact]
    public async Task OpenAddAccountDialog_SwitchesToAccountsTabAndOpensDialog()
    {
        var (vm, _, _) = await CreateVmWithCashAsync(0m);
        vm.OpenAddAccountDialogCommand.Execute(null);

        Assert.Equal(Assetra.WPF.Features.Portfolio.PortfolioTab.Accounts, vm.SelectedTab);
        Assert.True(vm.AddAssetDialog.IsAddDialogOpen);
        Assert.True(vm.AddAssetDialog.IsTypePickerStep);
        Assert.True(vm.AddAssetDialog.IsAccountDialogMode);

        vm.AddAssetDialog.SelectLiabilityTypeCommand.Execute("cash:銀行活存");
        Assert.Equal("cash", vm.AddAssetDialog.AddAssetType);
        Assert.Equal("銀行活存", vm.AddAssetDialog.AddSubtype);
        Assert.False(vm.AddAssetDialog.IsTypePickerStep);
    }

    [Fact]
    public void TradeRowViewModel_IsMetaOnly_TrueForSell()
    {
        // Sell is always meta-only (the lot it referenced was removed at sell time).
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "2330", Exchange: "TWSE", Name: "TSMC",
            Type: TradeType.Sell, TradeDate: DateTime.UtcNow,
            Price: 100, Quantity: 1000,
            RealizedPnl: 50_000m, RealizedPnlPct: 0.5m,
            PortfolioEntryId: Guid.NewGuid()));  // link present but entry already gone
        Assert.True(row.IsMetaOnlyEditType);
    }

    [Fact]
    public void TradeRowViewModel_IsMetaOnly_FalseForLegacyUnlinkedBuy()
    {
        // Source-of-truth pass：Buy 沒掛 PortfolioEntryId（legacy / imported）也視為
        // 可直接編輯。ConfirmTx 走 implicit-revision (delete-old + create-new) 路徑，
        // cost basis 從交易記錄重新 project 出來，不需要 PortfolioEntry 連結保護。
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "2330", Exchange: "TWSE", Name: "TSMC",
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 100, Quantity: 1000,
            RealizedPnl: null, RealizedPnlPct: null,
            PortfolioEntryId: null));
        Assert.False(row.IsMetaOnlyEditType);
    }

    [Fact]
    public void TradeRowViewModel_IsMetaOnly_FalseForLinkedBuy()
    {
        // Fresh Buy with lot link → full edit supported.
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "2330", Exchange: "TWSE", Name: "TSMC",
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 100, Quantity: 1000,
            RealizedPnl: null, RealizedPnlPct: null,
            PortfolioEntryId: Guid.NewGuid()));
        Assert.False(row.IsMetaOnlyEditType);
    }

    [Theory]
    [InlineData(TradeType.Income)]
    [InlineData(TradeType.CashDividend)]
    [InlineData(TradeType.Deposit)]
    [InlineData(TradeType.Withdrawal)]
    [InlineData(TradeType.LoanBorrow)]
    [InlineData(TradeType.LoanRepay)]
    public void TradeRowViewModel_IsMetaOnly_FalseForCashTypes(TradeType t)
    {
        // Cash-flow / loan types always support full edit.
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "N/A", Exchange: "", Name: "account",
            Type: t, TradeDate: DateTime.UtcNow,
            Price: 0, Quantity: 1,
            RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 1000));
        Assert.False(row.IsMetaOnlyEditType);
    }

    // DisplayAsset: unified asset column text

    [Theory]
    [InlineData(TradeType.Buy)]
    [InlineData(TradeType.Sell)]
    [InlineData(TradeType.CashDividend)]
    [InlineData(TradeType.StockDividend)]
    public void DisplayAsset_Stock_CombinesNameAndSymbol(TradeType t)
    {
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "00982A", Exchange: "TWSE",
            Name: "主動群益台灣強棒",
            Type: t, TradeDate: DateTime.UtcNow,
            Price: 18.04m, Quantity: 5000,
            RealizedPnl: null, RealizedPnlPct: null));
        Assert.Equal("主動群益台灣強棒 (00982A)", row.DisplayAsset);
    }

    [Theory]
    [InlineData(TradeType.Deposit)]
    [InlineData(TradeType.Withdrawal)]
    [InlineData(TradeType.LoanBorrow)]
    [InlineData(TradeType.LoanRepay)]
    public void DisplayAsset_CashAccount_ShowsNameOnly(TradeType t)
    {
        // Cash / loan trades have Symbol == Name (account label) → no bracketed suffix.
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "台新A 7y", Exchange: "",
            Name: "台新A 7y",
            Type: t, TradeDate: DateTime.UtcNow,
            Price: 0, Quantity: 1,
            RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 1000));
        Assert.Equal("台新A 7y", row.DisplayAsset);
    }

    [Fact]
    public void DisplayAsset_Income_ShowsNoteOnly()
    {
        // Income uses Note (e.g. "薪資") as Name; Symbol is empty.
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: string.Empty, Exchange: string.Empty,
            Name: "薪資",
            Type: TradeType.Income, TradeDate: DateTime.UtcNow,
            Price: 0, Quantity: 1,
            RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 50_000m, Note: "薪資"));
        Assert.Equal("薪資", row.DisplayAsset);
    }

    [Fact]
    public void DisplayAsset_StockWithEmptyName_FallsBackToSymbol()
    {
        // Corner case: stock trade with no resolved Name (e.g. legacy data) — just show symbol.
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "0050", Exchange: "TWSE",
            Name: string.Empty,
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 140, Quantity: 1000,
            RealizedPnl: null, RealizedPnlPct: null));
        Assert.Equal("0050", row.DisplayAsset);
    }

    [Fact]
    public void FxSettlementSummary_CrossCurrency_ShowsAuditContext()
    {
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "DRAM", Exchange: "NASDAQ",
            Name: "Roundhill Memory ETF",
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 50m, Quantity: 20,
            RealizedPnl: null, RealizedPnlPct: null,
            InstrumentCurrency: "USD",
            SettlementCurrency: "TWD",
            FxRate: 31.25m,
            FxRateDate: new DateOnly(2026, 5, 8),
            FxSource: "bot"));

        Assert.True(row.HasFxSettlementMetadata);
        Assert.Equal("USD → TWD · 31.2500 · 2026-05-08 · bot", row.FxSettlementSummary);
    }

    [Fact]
    public void FxSettlementSummary_SameCurrency_StaysHidden()
    {
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "0050", Exchange: "TWSE",
            Name: "元大台灣50",
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 140m, Quantity: 1000,
            RealizedPnl: null, RealizedPnlPct: null,
            InstrumentCurrency: "TWD",
            SettlementCurrency: "TWD"));

        Assert.False(row.HasFxSettlementMetadata);
        Assert.Equal(string.Empty, row.FxSettlementSummary);
    }

    // IsTransferLeg: Transfer-created Deposit/Withdrawal locked to meta-only

    [Fact]
    public void TradeRowViewModel_IsTransferLeg_DetectsOutgoingNote()
    {
        // Withdrawal with "轉帳 → X" note == source leg of a transfer.
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "TWD Savings", Exchange: "",
            Name: "TWD Savings",
            Type: TradeType.Withdrawal, TradeDate: DateTime.UtcNow,
            Price: 0, Quantity: 1,
            RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 30_000m,
            Note: "轉帳 → USD Savings"));
        Assert.True(row.IsTransferLeg);
        Assert.True(row.IsMetaOnlyEditType, "Transfer leg must inherit meta-only treatment");
    }

    [Fact]
    public void TradeRowViewModel_IsTransferLeg_DetectsIncomingNote()
    {
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "USD Savings", Exchange: "",
            Name: "USD Savings",
            Type: TradeType.Deposit, TradeDate: DateTime.UtcNow,
            Price: 0, Quantity: 1,
            RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 1_000m,
            Note: "轉帳 ← TWD Savings"));
        Assert.True(row.IsTransferLeg);
        Assert.True(row.IsMetaOnlyEditType);
    }

    [Fact]
    public void TradeRowViewModel_IsTransferLeg_FalseForRegularDeposit()
    {
        // Normal deposit (not from transfer) is freely editable.
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "TWD Savings", Exchange: "",
            Name: "TWD Savings",
            Type: TradeType.Deposit, TradeDate: DateTime.UtcNow,
            Price: 0, Quantity: 1,
            RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 5_000m, Note: "月薪"));
        Assert.False(row.IsTransferLeg);
        Assert.False(row.IsMetaOnlyEditType);
    }

    [Fact]
    public void TradeRowViewModel_IsTransferLeg_NullNote_False()
    {
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "x", Exchange: "", Name: "x",
            Type: TradeType.Withdrawal, TradeDate: DateTime.UtcNow,
            Price: 0, Quantity: 1,
            RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 100m));
        Assert.False(row.IsTransferLeg);
    }

    // ── TotalAmount：現金流符號規則（與 PrimaryCashDelta 同構）────────────

    [Fact]
    public void TotalAmount_Buy_IsNegative_InclCommission()
    {
        // Buy 的實付現金 = −(P×Q + 手續費) → 流出為負值
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "2330", Exchange: "TWSE", Name: "TSMC",
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 100, Quantity: 1000,
            RealizedPnl: null, RealizedPnlPct: null,
            Commission: 140m));
        Assert.Equal(-(100_000m + 140m), row.TotalAmount);
        Assert.False(row.IsAmountPositive);
    }

    [Fact]
    public void TotalAmount_Sell_IsPositive_NetOfCommission()
    {
        // Sell 的實收現金 = +(P×Q − 手續費與稅) → 流入為正值
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "2330", Exchange: "TWSE", Name: "TSMC",
            Type: TradeType.Sell, TradeDate: DateTime.UtcNow,
            Price: 100, Quantity: 1000,
            RealizedPnl: 0, RealizedPnlPct: 0,
            Commission: 440m));
        Assert.Equal(100_000m - 440m, row.TotalAmount);
        Assert.True(row.IsAmountPositive);
    }

    [Fact]
    public void TotalAmount_BuyWithoutCommission_UsesPriceTimesQty()
    {
        // Legacy 無手續費 (null) → 仍為負值，手續費部分退回 0
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "2330", Exchange: "TWSE", Name: "TSMC",
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 100, Quantity: 1000,
            RealizedPnl: null, RealizedPnlPct: null,
            Commission: null));
        Assert.Equal(-100_000m, row.TotalAmount);
    }

    [Theory]
    [InlineData(TradeType.Income)]
    [InlineData(TradeType.Deposit)]
    [InlineData(TradeType.LoanBorrow)]
    public void TotalAmount_Inflows_ArePositiveCashAmount(TradeType t)
    {
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "acct", Exchange: "", Name: "acct",
            Type: t, TradeDate: DateTime.UtcNow,
            Price: 0, Quantity: 1,
            RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 30_000m));
        Assert.Equal(30_000m, row.TotalAmount);
        Assert.True(row.IsAmountPositive);
    }

    [Theory]
    [InlineData(TradeType.Withdrawal)]
    [InlineData(TradeType.LoanRepay)]
    [InlineData(TradeType.Transfer)]
    public void TotalAmount_Outflows_AreNegativeCashAmount(TradeType t)
    {
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "acct", Exchange: "", Name: "acct",
            Type: t, TradeDate: DateTime.UtcNow,
            Price: 0, Quantity: 1,
            RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 5_000m));
        Assert.Equal(-5_000m, row.TotalAmount);
        Assert.False(row.IsAmountPositive);
    }

    [Fact]
    public void TotalAmount_CashDividend_IsPositive_PrefersCashAmount()
    {
        // CashDividend 優先用 CashAmount；legacy 無 CashAmount 時回退 P×Q
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "2330", Exchange: "TWSE", Name: "TSMC",
            Type: TradeType.CashDividend, TradeDate: DateTime.UtcNow,
            Price: 3, Quantity: 1000,
            RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 3_500m));
        Assert.Equal(3_500m, row.TotalAmount);
        Assert.True(row.IsAmountPositive);
    }

    [Fact]
    public void TotalAmount_StockDividend_IsZero()
    {
        // 配股無現金 → TotalAmount = 0；UI 以 signed-dash converter 顯示 "—"
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "2330", Exchange: "TWSE", Name: "TSMC",
            Type: TradeType.StockDividend, TradeDate: DateTime.UtcNow,
            Price: 0, Quantity: 100,
            RealizedPnl: null, RealizedPnlPct: null));
        Assert.Equal(0m, row.TotalAmount);
    }

    [Fact]
    public void DisplayAsset_StockWithSameNameAsSymbol_ShowsOnce()
    {
        // When Name and Symbol are identical (unusual but possible) don't duplicate.
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "2330", Exchange: "TWSE",
            Name: "2330",
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 600, Quantity: 1000,
            RealizedPnl: null, RealizedPnlPct: null));
        Assert.Equal("2330", row.DisplayAsset);
    }

    [Fact]
    public async Task AddRecord_AlwaysOpensTxDialog_RegardlessOfTab()
    {
        // New behaviour: the header "新增紀錄" button always routes to the record
        // dialog; account creation lives in the per-tab ghost buttons.
        var (vm, _, _, _) = await CreateVmWithLiabilityAsync(0m, 0m);

        vm.SelectedTab = PortfolioTab.Accounts;
        vm.AddRecordCommand.Execute(null);
        Assert.True(vm.Transaction.IsTxDialogOpen);
        Assert.False(vm.AddAssetDialog.IsAddDialogOpen);

        vm.Transaction.CloseTxDialogCommand.Execute(null);
        vm.SelectedTab = PortfolioTab.Liability;
        vm.AddRecordCommand.Execute(null);
        Assert.True(vm.Transaction.IsTxDialogOpen);
        Assert.False(vm.AddAssetDialog.IsAddDialogOpen);
    }

    // Plan B: full Buy/Sell/StockDividend edit

    /// <summary>
    /// Source-of-truth pass (commit 6836323): cash-flow edits are no longer meta-only.
    /// Changing TxAmount in edit mode promotes to implicit revision (delete-old +
    /// create-new under the hood), so the new amount projects into the balance and the
    /// original trade Id is replaced. This guards the "trades are foundational raw data"
    /// model: edit a row, the projection reconciles.
    /// </summary>
    [Fact]
    public async Task EditTrade_Income_ChangingAmountAppliesViaImplicitRevision()
    {
        var (vm, cashRepo, tradeRepo) = await CreateVmWithCashAsync(0m);
        vm.AddAssetDialog.AddAssetType = "cash";
        vm.AddAssetDialog.AddAccountName = "現金";
        await vm.AddAssetDialog.ConfirmAddCommand.ExecuteAsync(null);

        vm.Transaction.TxType = "income";
        vm.Transaction.TxAmount = "10000";
        vm.Transaction.TxCashAccount = vm.CashAccounts.First();
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);
        Assert.Equal(10_000m, vm.CashAccounts[0].Balance);
        var originalTrade = vm.Trades.Single(t => t.IsIncome);

        // Open edit mode; Income is non-meta-only, so economic fields are live and ConfirmTx
        // promotes the save to implicit revision.
        vm.Transaction.EditTradeCommand.Execute(originalTrade);
        Assert.True(vm.Transaction.AreEconomicFieldsEditable);
        Assert.False(vm.Transaction.IsEditingMetaOnly);
        vm.Transaction.TxAmount = "99999";
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        // Balance reflects the new amount; original trade Id is replaced by the revision.
        Assert.Equal(99_999m, vm.CashAccounts[0].Balance);
        Assert.DoesNotContain(vm.Trades, t => t.Id == originalTrade.Id);
        Assert.Single(vm.Trades.Where(t => t.IsIncome));
    }

    [Fact]
    public async Task ConfirmTx_Income_WhenNotUseCashAccount_RecordsWithoutTouchingBalance()
    {
        // WHY: 收入「存入帳戶」取消勾選＝不連動現金——仍記錄一筆收入，但不影響任何帳戶餘額。
        var (vm, _, _) = await CreateVmWithCashAsync(0m);
        vm.AddAssetDialog.AddAssetType = "cash";
        vm.AddAssetDialog.AddAccountName = "現金";
        await vm.AddAssetDialog.ConfirmAddCommand.ExecuteAsync(null);

        vm.Transaction.TxType = "income";
        vm.Transaction.TxAmount = "10000";
        vm.Transaction.TxCashAccount = vm.CashAccounts.First();
        vm.Transaction.TxUseCashAccount = false;   // 不連動

        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Empty(vm.Transaction.TxError);
        Assert.Equal(0m, vm.CashAccounts[0].Balance);     // 餘額不變
        Assert.Single(vm.Trades.Where(t => t.IsIncome));  // 仍記錄一筆收入
    }

    [Fact]
    public async Task TradeRowViewModel_PortfolioEntryId_RoundTripsThroughRepository()
    {
        // Guards the schema round-trip: the new column must persist and come back.
        var (vm, _, tradeRepo, _) = await CreateVmWithLiabilityAsync(0m, 0m);
        var entryId = Guid.NewGuid();
        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: "2330", Exchange: "TWSE", Name: "TSMC",
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 100, Quantity: 1000,
            RealizedPnl: null, RealizedPnlPct: null,
            PortfolioEntryId: entryId));
        var loaded = (await tradeRepo.GetAllAsync()).Single(t => t.Type == TradeType.Buy);
        Assert.Equal(entryId, loaded.PortfolioEntryId);
    }

    [Fact]
    public async Task EditTrade_SellRow_PopulatesBothCashAccountFields()
    {
        // Regression: earlier only SellCashAccount was populated, so the dialog (which
        // binds TxCashAccount) showed an empty dropdown during Sell edit.
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(10_000m);
        var cashAcc = vm.CashAccounts.First();

        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: "2330", Exchange: "TWSE", Name: "TSMC",
            Type: TradeType.Sell, TradeDate: DateTime.UtcNow,
            Price: 650, Quantity: 1000,
            RealizedPnl: 50_000m, RealizedPnlPct: 8.3m,
            CashAccountId: cashAcc.Id,
            PortfolioEntryId: Guid.NewGuid()));
        await vm.LoadTradesAsyncForTest();

        vm.Transaction.EditTradeCommand.Execute(vm.Trades.First(t => t.Type == TradeType.Sell));

        Assert.Equal(cashAcc.Id, vm.Transaction.TxCashAccount?.Id);
        Assert.Equal(cashAcc.Id, vm.SellPanel.SellCashAccount?.Id);
        Assert.True(vm.Transaction.TxUseCashAccount);
    }

    [Fact]
    public async Task TradesTabPanel_ClickTradeRow_UsesClickedRowForEditPrefill()
    {
        // Regression guard: the trades tab should edit the row that was actually clicked,
        // not a previously selected row or the first matching item in the collection.
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(10_000m);
        var cashAcc = vm.CashAccounts.First();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        await tradeRepo.AddAsync(new Trade(
            Id: firstId, Symbol: "2330", Exchange: "TWSE", Name: "TSMC",
            Type: TradeType.Sell, TradeDate: new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            Price: 650, Quantity: 1000,
            RealizedPnl: 50_000m, RealizedPnlPct: 8.3m,
            CashAccountId: cashAcc.Id,
            PortfolioEntryId: Guid.NewGuid(),
            Note: "first sell"));

        await tradeRepo.AddAsync(new Trade(
            Id: secondId, Symbol: "0050", Exchange: "TWSE", Name: "元大台灣50",
            Type: TradeType.Sell, TradeDate: new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc),
            Price: 188.5m, Quantity: 2000,
            RealizedPnl: 12_345m, RealizedPnlPct: 3.2m,
            CashAccountId: cashAcc.Id,
            PortfolioEntryId: Guid.NewGuid(),
            Note: "second sell"));
        await vm.LoadTradesAsyncForTest();

        var clickedRow = vm.Trades.Single(t => t.Id == secondId);
        var opened = TradesTabPanel.TryOpenTradeEditor(vm, clickedRow);

        Assert.True(opened);
        Assert.True(vm.Transaction.IsTxDialogOpen);
        Assert.Equal(secondId, vm.Transaction.EditingTradeId);
        Assert.Equal("sell", vm.Transaction.TxType);
        Assert.Equal("second sell", vm.Transaction.TxNote);
        Assert.Equal("188.5000", vm.Transaction.TxAmount);
        Assert.Equal("2000", vm.Transaction.Sell.Quantity);
        Assert.Equal("188.5000", vm.SellPanel.SellPriceInput);
        Assert.Equal(cashAcc.Id, vm.Transaction.TxCashAccount?.Id);
    }

    [Fact]
    public async Task EditTrade_CashDividendRow_SetsTxUseCashAccountFromLink()
    {
        // Regression: CashDividend edit didn't flip TxUseCashAccount, so the checkbox
        // visual was wrong when the original trade had a linked cash account.
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(10_000m);
        var cashAcc = vm.CashAccounts.First();

        // Seed a position first so Div.Position can find it
        var pos = new PortfolioRowViewModel
        { Id = Guid.NewGuid(), Symbol = "0050", Quantity = 1000, BuyPrice = 140 };
        vm.Internal_AddPosition(pos);

        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: "0050", Exchange: "TWSE", Name: "元大台灣50",
            Type: TradeType.CashDividend, TradeDate: DateTime.UtcNow,
            Price: 2.5m, Quantity: 1000,
            RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 2500m,
            CashAccountId: cashAcc.Id));
        await vm.LoadTradesAsyncForTest();

        vm.Transaction.EditTradeCommand.Execute(vm.Trades.First(t => t.Type == TradeType.CashDividend));

        Assert.Equal(cashAcc.Id, vm.Transaction.TxCashAccount?.Id);
        Assert.True(vm.Transaction.TxUseCashAccount);
    }

    [Fact]
    public async Task EditTrade_TransferLeg_OpensInMetaOnlyMode()
    {
        // Regression: transfer-created Withdrawal/Deposit trades were edit-loose, which
        // could break the source/target pair invariant.
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(10_000m);

        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: "TWD Savings", Exchange: "",
            Name: "TWD Savings", Type: TradeType.Withdrawal,
            TradeDate: DateTime.UtcNow,
            Price: 0, Quantity: 1, RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 30_000m,
            CashAccountId: vm.CashAccounts.First().Id,
            Note: "轉帳 → USD Savings"));
        await vm.LoadTradesAsyncForTest();

        var leg = vm.Trades.First(t => t.IsTransferLeg);
        vm.Transaction.EditTradeCommand.Execute(leg);

        Assert.True(vm.Transaction.IsEditingMetaOnly);
        Assert.False(vm.Transaction.AreEconomicFieldsEditable);
    }

    [Fact]
    public async Task EditTrade_SellRow_DialogOpensInMetaOnlyMode()
    {
        // Sell trades are historical (entry gone). EditTrade must flag meta-only
        // so the dialog XAML can disable price/position/cash-account inputs.
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(0m);
        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: "2330", Exchange: "TWSE", Name: "TSMC",
            Type: TradeType.Sell, TradeDate: DateTime.UtcNow,
            Price: 650, Quantity: 1000,
            RealizedPnl: 50_000m, RealizedPnlPct: 8.3m,
            PortfolioEntryId: Guid.NewGuid()));
        await vm.LoadTradesAsyncForTest();

        var sellInCollection = vm.Trades.First(t => t.Type == TradeType.Sell);
        vm.Transaction.EditTradeCommand.Execute(sellInCollection);

        Assert.True(vm.Transaction.IsTxDialogOpen);
        Assert.True(vm.Transaction.IsEditMode);
        Assert.True(vm.Transaction.IsEditingMetaOnly, "Sell edit should be meta-only");
        Assert.False(vm.Transaction.AreEconomicFieldsEditable);
        Assert.Equal("sell", vm.Transaction.TxType);
    }

    [Fact]
    public async Task EditTrade_SellRow_KeepsStoredPriceEvenWhenMatchingPositionHasCurrentPrice()
    {
        // Regression: when a same-symbol holding still exists, selecting Sell.Position
        // used to auto-fill TxAmount from CurrentPrice and could briefly or permanently
        // replace the stored sell price during edit prefill.
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(10_000m);
        vm.Internal_AddPosition(new PortfolioRowViewModel
        {
            Id = Guid.NewGuid(),
            Symbol = "2330",
            Quantity = 1000,
            BuyPrice = 500m,
            CurrentPrice = 777m
        });

        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: "2330", Exchange: "TWSE", Name: "TSMC",
            Type: TradeType.Sell, TradeDate: DateTime.UtcNow,
            Price: 650, Quantity: 1000,
            RealizedPnl: 50_000m, RealizedPnlPct: 8.3m,
            PortfolioEntryId: Guid.NewGuid()));
        await vm.LoadTradesAsyncForTest();

        var sellInCollection = vm.Trades.First(t => t.Type == TradeType.Sell);
        vm.Transaction.EditTradeCommand.Execute(sellInCollection);

        Assert.Equal("650.0000", vm.Transaction.TxAmount);
        Assert.Equal("650.0000", vm.SellPanel.SellPriceInput);
    }

    [Fact]
    public async Task EditTrade_BuyWithLink_DialogOpensWithEditableEconomicFields()
    {
        // Source-of-truth pass (commit 6836323): Buy is no longer meta-only. Clicking edit
        // opens the dialog with the normal form fields live and pre-filled — ConfirmTx
        // promotes the save to implicit revision (delete-old + create-new). The explicit
        // 修訂 button is reserved for users who want to keep both records.
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(0m);
        var entryId = Guid.NewGuid();
        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: "00982A", Exchange: "TWSE", Name: "主動群益台灣強棒",
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 18.04m, Quantity: 5000,
            RealizedPnl: null, RealizedPnlPct: null,
            PortfolioEntryId: entryId));
        await vm.LoadTradesAsyncForTest();

        var buyRow = vm.Trades.First(t => t.Type == TradeType.Buy);
        vm.Transaction.EditTradeCommand.Execute(buyRow);

        Assert.Equal("buy", vm.Transaction.TxType);
        Assert.True(vm.Transaction.IsEditMode);
        Assert.False(vm.Transaction.IsEditingMetaOnly);
        Assert.True(vm.Transaction.AreEconomicFieldsEditable);
        Assert.False(vm.Transaction.ShowEditLockedSummary);
        // Economic fields are live and pre-filled from the existing trade.
        Assert.Equal("00982A", vm.AddAssetDialog.AddSymbol);
        Assert.Equal("18.0400", vm.AddAssetDialog.AddPrice);
        Assert.Equal("5000", vm.AddAssetDialog.AddQuantity);
    }

    [Fact]
    public async Task CreateRevision_FromEditedTrade_PreservesPrefillAndUnlocksCoreFields()
    {
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(0m);
        var entryId = Guid.NewGuid();
        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: "00982A", Exchange: "TWSE", Name: "主動群益台灣強棒",
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 18.04m, Quantity: 5000,
            RealizedPnl: null, RealizedPnlPct: null,
            PortfolioEntryId: entryId));
        await vm.LoadTradesAsyncForTest();

        var buyRow = vm.Trades.First(t => t.Type == TradeType.Buy);
        vm.Transaction.EditTradeCommand.Execute(buyRow);
        vm.Transaction.CreateRevisionCommand.Execute(null);

        Assert.True(vm.Transaction.IsTxDialogOpen);
        Assert.True(vm.Transaction.IsRevisionMode);
        Assert.False(vm.Transaction.IsEditMode);
        Assert.True(vm.Transaction.AreEconomicFieldsEditable);
        Assert.Equal("buy", vm.Transaction.TxType);
        Assert.Equal("00982A", vm.AddAssetDialog.AddSymbol);
        Assert.Equal("18.0400", vm.AddAssetDialog.AddPrice);
        Assert.Equal("5000", vm.AddAssetDialog.AddQuantity);
    }

    [Fact]
    public async Task CreateRevision_SaveThenKeepBoth_PreservesOriginalAndRevision()
    {
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(0m);
        var entryId = Guid.NewGuid();
        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: "00982A", Exchange: "TWSE", Name: "主動群益台灣強棒",
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 18.04m, Quantity: 5000,
            RealizedPnl: null, RealizedPnlPct: null,
            PortfolioEntryId: entryId));
        await vm.LoadTradesAsyncForTest();

        var original = vm.Trades.First(t => t.Type == TradeType.Buy);
        vm.Transaction.EditTradeCommand.Execute(original);
        vm.Transaction.CreateRevisionCommand.Execute(null);
        vm.AddAssetDialog.AddPrice = "19.5000";

        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.True(vm.Transaction.IsRevisionReplacePromptOpen);
        Assert.True(vm.Transaction.IsTxDialogOpen);

        vm.Transaction.KeepBothRecordsCommand.Execute(null);
        await vm.LoadTradesAsyncForTest();

        var buys = vm.Trades.Where(t => t.Type == TradeType.Buy && t.Symbol == "00982A").ToList();
        Assert.Equal(2, buys.Count);
        Assert.Contains(buys, t => t.Id == original.Id);
        Assert.Contains(buys, t => t.Price == 19.5m);
    }

    [Fact]
    public async Task CreateRevision_SaveThenReplaceOriginal_RemovesSourceTrade()
    {
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(0m);
        var entryId = Guid.NewGuid();
        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: "00982A", Exchange: "TWSE", Name: "主動群益台灣強棒",
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 18.04m, Quantity: 5000,
            RealizedPnl: null, RealizedPnlPct: null,
            PortfolioEntryId: entryId));
        await vm.LoadTradesAsyncForTest();

        var original = vm.Trades.First(t => t.Type == TradeType.Buy);
        vm.Transaction.EditTradeCommand.Execute(original);
        vm.Transaction.CreateRevisionCommand.Execute(null);
        vm.AddAssetDialog.AddPrice = "19.5000";

        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);
        await vm.Transaction.ReplaceOriginalRecordCommand.ExecuteAsync(null);
        await vm.LoadTradesAsyncForTest();

        var buys = vm.Trades.Where(t => t.Type == TradeType.Buy && t.Symbol == "00982A").ToList();
        Assert.Single(buys);
        Assert.DoesNotContain(buys, t => t.Id == original.Id);
        Assert.Equal(19.5m, buys[0].Price);
    }

    [Fact]
    public async Task CreateRevision_ForTransfer_PreservesOriginalAndCreatesNewRecord()
    {
        var (vm, assetRepo, tradeRepo) = await CreateVmWithCashAsync(50_000m);
        var secondAccount = new AssetItem(
            Guid.NewGuid(), "Richart USD", FinancialType.Asset, null, "USD",
            DateOnly.FromDateTime(DateTime.Today));
        assetRepo.Store.Add(secondAccount);
        await SeedCashBaselineAsync(tradeRepo, secondAccount.Id, secondAccount.Name, 30_000m);
        await vm.LoadAsync();

        var src = vm.CashAccounts.First(c => c.Name == "永豐 USD");
        var dst = vm.CashAccounts.First(c => c.Name == "Richart USD");
        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: src.Name, Exchange: string.Empty,
            Name: src.Name, Type: TradeType.Transfer,
            TradeDate: DateTime.UtcNow,
            Price: 0, Quantity: 1, RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 30_000m, CashAccountId: src.Id, ToCashAccountId: dst.Id, Note: "seed transfer"));
        await vm.LoadTradesAsyncForTest();

        var original = vm.Trades.First(t => t.Type == TradeType.Transfer);
        vm.Transaction.EditTradeCommand.Execute(original);
        vm.Transaction.CreateRevisionCommand.Execute(null);
        vm.Transaction.Transfer.TargetAmount = "30000";

        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);
        vm.Transaction.KeepBothRecordsCommand.Execute(null);
        await vm.LoadTradesAsyncForTest();

        var transfers = vm.Trades.Where(t => t.Type == TradeType.Transfer).ToList();
        Assert.Equal(2, transfers.Count);
        Assert.Contains(transfers, t => t.Id == original.Id);
        Assert.Contains(transfers, t => t.CashAmount == 30_000m && t.Note == "seed transfer");
        Assert.Contains(tradeRepo.Store, t => t.Type == TradeType.Transfer && t.CashAmount == 30_000m && t.ToCashAccountId == dst.Id && t.Note == "seed transfer");
    }

    [Fact]
    public async Task EditTrade_NativeTransfer_LocksSourceButKeepsFieldsEditable()
    {
        // WHY: 編輯既有轉帳不該讓使用者不小心改掉來源帳戶 → 來源鎖定為唯讀摘要；
        //      但金額/轉入帳戶等仍要能改（native transfer 非 meta-only）。
        var (vm, assetRepo, tradeRepo) = await CreateVmWithCashAsync(50_000m);
        var secondAccount = new AssetItem(
            Guid.NewGuid(), "Richart USD", FinancialType.Asset, null, "USD",
            DateOnly.FromDateTime(DateTime.Today));
        assetRepo.Store.Add(secondAccount);
        await SeedCashBaselineAsync(tradeRepo, secondAccount.Id, secondAccount.Name, 30_000m);
        await vm.LoadAsync();

        var src = vm.CashAccounts.First(c => c.Name == "永豐 USD");
        var dst = vm.CashAccounts.First(c => c.Name == "Richart USD");
        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: src.Name, Exchange: string.Empty,
            Name: src.Name, Type: TradeType.Transfer,
            TradeDate: DateTime.UtcNow,
            Price: 0, Quantity: 1, RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 30_000m, CashAccountId: src.Id, ToCashAccountId: dst.Id, Note: "seed transfer"));
        await vm.LoadTradesAsyncForTest();
        var original = vm.Trades.First(t => t.Type == TradeType.Transfer);

        vm.Transaction.EditTradeCommand.Execute(original);

        Assert.True(vm.Transaction.IsAssetContextLocked);       // 來源鎖定（顯示為唯讀摘要）
        Assert.False(vm.Transaction.ShowAssetSelector);          // 上方「選擇資產」選擇器隱藏
        Assert.True(vm.Transaction.AreEconomicFieldsEditable);   // 金額/轉入帳戶等仍可改
    }

    [Fact]
    public void TradeRowViewModel_Transfer_SignFlipsForIncomingPerspective()
    {
        // WHY: 同一筆轉帳，來源帳戶視角為流出（−），目標帳戶（收款方）視角為流入（+）。
        var t = new Trade(
            Id: Guid.NewGuid(), Symbol: "永豐", Exchange: string.Empty,
            Name: "永豐 → 蝦皮", Type: TradeType.Transfer, TradeDate: DateTime.UtcNow,
            Price: 0, Quantity: 1, RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 50_000m, CashAccountId: Guid.NewGuid(), ToCashAccountId: Guid.NewGuid());

        var source = new TradeRowViewModel(t);
        Assert.False(source.IncomingTransferView);
        Assert.Equal(-50_000m, source.TotalAmount);          // 來源：流出

        var incoming = source.AsIncomingTransferView();
        Assert.NotSame(source, incoming);                    // 產生副本，不污染原 row
        Assert.True(incoming.IncomingTransferView);
        Assert.Equal(50_000m, incoming.TotalAmount);         // 收款：流入
    }

    [Fact]
    public async Task SelectedCashTrades_OnTargetAccount_ShowsIncomingTransferAsPositive()
    {
        // WHY: 轉帳的目標帳戶（收款方）的交易記錄也要看得到這筆、且金額為正（流入）。
        //      之前只用 CashAccountId 過濾 → 目標帳戶看不到轉入紀錄（錢看起來沒進來、實際餘額已正確）。
        var (vm, assetRepo, tradeRepo) = await CreateVmWithCashAsync(50_000m);
        var secondAccount = new AssetItem(
            Guid.NewGuid(), "Richart USD", FinancialType.Asset, null, "USD",
            DateOnly.FromDateTime(DateTime.Today));
        assetRepo.Store.Add(secondAccount);
        await SeedCashBaselineAsync(tradeRepo, secondAccount.Id, secondAccount.Name, 30_000m);
        await vm.LoadAsync();

        var src = vm.CashAccounts.First(c => c.Name == "永豐 USD");
        var dst = vm.CashAccounts.First(c => c.Name == "Richart USD");
        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: src.Name, Exchange: string.Empty,
            Name: $"{src.Name} → {dst.Name}", Type: TradeType.Transfer, TradeDate: DateTime.UtcNow,
            Price: 0, Quantity: 1, RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 30_000m, CashAccountId: src.Id, ToCashAccountId: dst.Id, Note: "轉帳"));
        await vm.LoadTradesAsyncForTest();

        // 目標帳戶（收款方）：看得到、且為流入（+）
        vm.SelectedCashRow = dst;
        var atTarget = vm.SelectedCashTrades.Single(t => t.IsTransfer);
        Assert.True(atTarget.IncomingTransferView);
        Assert.Equal(30_000m, atTarget.TotalAmount);

        // 來源帳戶：同一筆顯示為流出（−）
        vm.SelectedCashRow = src;
        var atSource = vm.SelectedCashTrades.Single(t => t.IsTransfer);
        Assert.False(atSource.IncomingTransferView);
        Assert.Equal(-30_000m, atSource.TotalAmount);
    }

    [Fact]
    public async Task CreateRevision_ForCashDividend_PreservesOriginalAndCreatesNewRecord()
    {
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(10_000m);
        var cashAcc = vm.CashAccounts.First();
        vm.Internal_AddPosition(new PortfolioRowViewModel
        {
            Id = Guid.NewGuid(),
            Symbol = "0050",
            Name = "元大台灣50",
            Exchange = "TWSE",
            Quantity = 1000,
            BuyPrice = 140
        });

        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: "0050", Exchange: "TWSE", Name: "元大台灣50",
            Type: TradeType.CashDividend, TradeDate: DateTime.UtcNow,
            Price: 2.5m, Quantity: 1000,
            RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 2500m,
            CashAccountId: cashAcc.Id));
        await vm.LoadTradesAsyncForTest();

        var original = vm.Trades.First(t => t.Type == TradeType.CashDividend);
        vm.Transaction.EditTradeCommand.Execute(original);
        vm.Transaction.CreateRevisionCommand.Execute(null);
        vm.Transaction.Div.PerShare = "3.0";

        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);
        vm.Transaction.KeepBothRecordsCommand.Execute(null);
        await vm.LoadTradesAsyncForTest();

        var dividends = vm.Trades.Where(t => t.Type == TradeType.CashDividend && t.Symbol == "0050").ToList();
        Assert.Equal(2, dividends.Count);
        Assert.Contains(dividends, t => t.Id == original.Id);
        Assert.Contains(dividends, t => t.Price == 3.0m && t.CashAmount == 3000m);
    }

    [Fact]
    public async Task ReplaceOriginalRecord_WhenSourceMissing_ShowsPromptError()
    {
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(0m);
        var entryId = Guid.NewGuid();
        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: "00982A", Exchange: "TWSE", Name: "主動群益台灣強棒",
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 18.04m, Quantity: 5000,
            RealizedPnl: null, RealizedPnlPct: null,
            PortfolioEntryId: entryId));
        await vm.LoadTradesAsyncForTest();

        var original = vm.Trades.First(t => t.Type == TradeType.Buy);
        vm.Transaction.EditTradeCommand.Execute(original);
        vm.Transaction.CreateRevisionCommand.Execute(null);
        vm.AddAssetDialog.AddPrice = "19.5000";

        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);
        tradeRepo.Store.RemoveAll(t => t.Id == original.Id);
        await vm.LoadTradesAsyncForTest();
        await vm.Transaction.ReplaceOriginalRecordCommand.ExecuteAsync(null);

        Assert.True(vm.Transaction.IsRevisionReplacePromptOpen);
        Assert.NotEmpty(vm.Transaction.RevisionReplacePromptError);
    }

    [Fact]
    public async Task ApplyTrades_NotifiesSelectedPositionTrades()
    {
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(0m);
        var position = new PortfolioRowViewModel
        {
            Id = Guid.NewGuid(),
            Symbol = "0056",
            Exchange = "TWSE",
            Name = "元大高股息",
            Quantity = 1000,
            BuyPrice = 35m,
        };
        vm.Internal_AddPosition(position);
        vm.SelectedPositionRow = position;

        var changed = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.PropertyName))
                changed.Add(e.PropertyName);
        };

        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: "0056", Exchange: "TWSE", Name: "元大高股息",
            Type: TradeType.CashDividend, TradeDate: DateTime.UtcNow,
            Price: 1.2m, Quantity: 1000,
            RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 1200m));

        await vm.LoadTradesAsyncForTest();

        Assert.Contains(nameof(vm.SelectedPositionTrades), changed);
        Assert.Single(vm.SelectedPositionTrades);
    }

    [Fact]
    public async Task ConfirmTx_StaleEditTarget_DoesNotCreateDuplicateTrade()
    {
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(10_000m);
        var cash = vm.CashAccounts.First();
        var position = new PortfolioRowViewModel
        {
            Id = Guid.NewGuid(),
            Symbol = "0056",
            Exchange = "TWSE",
            Name = "元大高股息",
            Quantity = 1000,
            BuyPrice = 35m,
        };
        vm.Internal_AddPosition(position);

        var originalId = Guid.NewGuid();
        await tradeRepo.AddAsync(new Trade(
            Id: originalId, Symbol: "0056", Exchange: "TWSE", Name: "元大高股息",
            Type: TradeType.CashDividend, TradeDate: DateTime.UtcNow,
            Price: 1.2m, Quantity: 1000,
            RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 1200m,
            CashAccountId: cash.Id));
        await vm.LoadTradesAsyncForTest();

        var staleRow = vm.Trades.Single(t => t.Id == originalId);
        tradeRepo.Store.RemoveAll(t => t.Id == originalId);
        await vm.LoadTradesAsyncForTest();

        vm.Transaction.EditTradeCommand.Execute(staleRow);
        vm.Transaction.Div.PerShare = "2.0";
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.DoesNotContain(tradeRepo.Store, t => t.Type == TradeType.CashDividend);
        Assert.False(vm.Transaction.IsTxDialogOpen);
        Assert.Contains("不存在", vm.Transaction.TxError);
    }

    [Fact]
    public async Task EditTrade_CloseDialog_ClearsRevisionState()
    {
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(0m);
        var entryId = Guid.NewGuid();
        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: "00982A", Exchange: "TWSE", Name: "主動群益台灣強棒",
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 18.04m, Quantity: 5000,
            RealizedPnl: null, RealizedPnlPct: null,
            PortfolioEntryId: entryId));
        await vm.LoadTradesAsyncForTest();

        var original = vm.Trades.First(t => t.Type == TradeType.Buy);
        vm.Transaction.EditTradeCommand.Execute(original);
        vm.Transaction.CreateRevisionCommand.Execute(null);
        vm.Transaction.CloseTxDialogCommand.Execute(null);

        Assert.False(vm.Transaction.IsTxDialogOpen);
        Assert.False(vm.Transaction.IsRevisionMode);
        Assert.False(vm.Transaction.IsRevisionReplacePromptOpen);
        Assert.False(vm.Transaction.IsEditMode);
        Assert.True(string.IsNullOrEmpty(vm.Transaction.RevisionReplacePromptError));
    }

    [Fact]
    public async Task ConfirmAddWatchlist_UsesInjectedAddAssetWorkflow()
    {
        // WHY: the title-bar "新增投資" flow is separate from AddAssetDialog.
        // Production DI must inject the same add-asset workflow into PortfolioViewModel,
        // otherwise every symbol, including valid US ETFs, shows "服務未就緒".
        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync(Array.Empty<PortfolioEntry>());

        var search = new Mock<IStockSearchService>();
        search.Setup(s => s.GetExchange("EUV")).Returns("NASDAQ");
        search.Setup(s => s.GetName("EUV")).Returns("EUV");

        var workflow = new Mock<IAddAssetWorkflowService>();
        workflow
            .Setup(w => w.EnsureStockEntryAsync(It.IsAny<EnsureStockEntryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EnsureStockEntryRequest request, CancellationToken _) =>
                new PortfolioEntry(
                    Guid.NewGuid(),
                    request.Symbol,
                    request.Exchange ?? "NASDAQ",
                    request.AssetType,
                    request.Name ?? request.Symbol,
                    request.Currency ?? "USD"));

        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);
        var vm = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object, Trade: new FakeTradeRepo()),
            new PortfolioServices(
                SilentStockService().Object,
                search.Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                AddAsset: workflow.Object),
            new PortfolioUiServices(ImmediateScheduler.Instance));

        vm.OpenAddWatchlistDialogCommand.Execute(null);
        vm.WatchlistSymbol = "EUV";
        vm.WatchlistAssetType = AssetType.Etf;
        vm.WatchlistCurrency = "USD";

        await vm.ConfirmAddWatchlistCommand.ExecuteAsync(null);

        Assert.Empty(vm.WatchlistError);
        Assert.False(vm.IsAddWatchlistOpen);
        workflow.Verify(w => w.EnsureStockEntryAsync(
            It.Is<EnsureStockEntryRequest>(r =>
                r.Symbol == "EUV"
                && r.Exchange == "NASDAQ"
                && r.AssetType == AssetType.Etf
                && r.Currency == "USD"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Watchlist_UsSymbolWithoutTwelveDataKey_ShowsFallbackNotice_ButStillAdds()
    {
        // WHY: 美股代號在未設定 Twelve Data 金鑰時，使用者需要被告知會走 Yahoo 後備來源
        // （可能延遲/受限），但這只是 advisory — 加入流程必須照常完成（qty-0 entry 允許）。
        // 沒有注入 IAppSettingsService（Settings=null）時，視同未填金鑰。
        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync(Array.Empty<PortfolioEntry>());

        var search = new Mock<IStockSearchService>();
        search.Setup(s => s.GetExchange("AAPL")).Returns("NASDAQ");

        var workflow = new Mock<IAddAssetWorkflowService>();
        // 代號已知、目錄已下載 → 唯一該出現的提示是「無金鑰 → Yahoo 後備」。
        workflow.Setup(w => w.CheckWatchlistSymbol("AAPL", It.IsAny<string?>()))
            .Returns(new WatchlistSymbolReadiness(IsResolved: true, UsDirectoryReady: true));
        workflow.Setup(w => w.EnsureStockEntryAsync(It.IsAny<EnsureStockEntryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EnsureStockEntryRequest request, CancellationToken _) =>
                new PortfolioEntry(Guid.NewGuid(), request.Symbol, request.Exchange ?? "NASDAQ"));

        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);
        var vm = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object, Trade: new FakeTradeRepo()),
            new PortfolioServices(
                SilentStockService().Object,
                search.Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                AddAsset: workflow.Object),
            new PortfolioUiServices(ImmediateScheduler.Instance));

        vm.OpenAddWatchlistDialogCommand.Execute(null);
        vm.WatchlistSymbol = "AAPL";

        // Advisory notice computed on symbol change — mentions the key + fallback source.
        Assert.False(string.IsNullOrEmpty(vm.WatchlistNotice));
        Assert.Contains("Yahoo", vm.WatchlistNotice!);
        Assert.Contains("金鑰", vm.WatchlistNotice!);

        await vm.ConfirmAddWatchlistCommand.ExecuteAsync(null);

        // Notice never blocks: the add still runs and the dialog closes.
        Assert.Empty(vm.WatchlistError);
        Assert.False(vm.IsAddWatchlistOpen);
        workflow.Verify(w => w.EnsureStockEntryAsync(
            It.Is<EnsureStockEntryRequest>(r => r.Symbol == "AAPL"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Watchlist_UnresolvedSymbolWithLoadedDirectory_ShowsNotFoundNotice()
    {
        // WHY: 目錄已下載但查無此代號 → 軟性提示「找不到此代號」。仍不阻擋加入
        // （目錄可能過期），只是引導使用者確認拼寫。
        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync(Array.Empty<PortfolioEntry>());

        var search = new Mock<IStockSearchService>();
        search.Setup(s => s.GetExchange("ZZZZ")).Returns((string?)null);

        var workflow = new Mock<IAddAssetWorkflowService>();
        workflow.Setup(w => w.InferExchange("ZZZZ")).Returns("NASDAQ");
        // 目錄已下載 (UsDirectoryReady) 但解析不到 (IsResolved=false) → not-found 提示。
        workflow.Setup(w => w.CheckWatchlistSymbol("ZZZZ", It.IsAny<string?>()))
            .Returns(new WatchlistSymbolReadiness(IsResolved: false, UsDirectoryReady: true));

        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);
        var vm = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object, Trade: new FakeTradeRepo()),
            new PortfolioServices(
                SilentStockService().Object,
                search.Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                AddAsset: workflow.Object),
            new PortfolioUiServices(ImmediateScheduler.Instance));

        vm.OpenAddWatchlistDialogCommand.Execute(null);
        vm.WatchlistSymbol = "ZZZZ";

        Assert.False(string.IsNullOrEmpty(vm.WatchlistNotice));
        Assert.Contains("找不到此代號", vm.WatchlistNotice!);
    }

    [Fact]
    public async Task ClosePriceLookup_DoesNotReplaceUserTypedUnitPrice()
    {
        // Regression: the asynchronous close-price lookup can finish after the
        // user has typed the broker-confirmed unit price. The lookup is only a
        // convenience default; once the field has user input it must not win.
        var delayedWorkflow = new DelayedClosePriceWorkflow(delayMs: 50, lookupPrice: 60.5m);
        var vm = new AddAssetDialogViewModel(
            delayedWorkflow,
            new NoopAccountUpsertWorkflow(),
            new NoopCreditCardMutationWorkflow());

        vm.AddAssetType = "stock";
        vm.AddSymbol = "DRAM";
        vm.AddPrice = "60";

        await Task.Delay(120);

        Assert.Equal("60", vm.AddPrice);
    }

    [Fact]
    public async Task EditTrade_BuyWithLink_DoesNotReplaceStoredPriceWithHistoricalLookup()
    {
        // Regression: edit prefill used to trigger async close-price lookup via AddBuyDate,
        // which could overwrite the persisted trade price a moment after the dialog opened.
        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync([]);

        var assetRepo = new FakeAssetRepo();
        var tradeRepo = new FakeTradeRepo();
        var search = new Mock<IStockSearchService>();
        search.Setup(s => s.GetExchange("00982A")).Returns("TWSE");
        var delayedWorkflow = new DelayedClosePriceWorkflow(delayMs: 50, lookupPrice: 99.99m);
        var addAssetDialog = new AddAssetDialogViewModel(delayedWorkflow, new NoopAccountUpsertWorkflow(), new NoopCreditCardMutationWorkflow());
        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);

        var vm = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object, Trade: tradeRepo, Asset: assetRepo),
            new PortfolioServices(
                SilentStockService().Object,
                search.Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                AddAssetDialog: addAssetDialog),
            new PortfolioUiServices(ImmediateScheduler.Instance));
        await vm.LoadAsync();

        var entryId = Guid.NewGuid();
        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: "00982A", Exchange: "TWSE", Name: "主動群益台灣強棒",
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 18.04m, Quantity: 5000,
            RealizedPnl: null, RealizedPnlPct: null,
            PortfolioEntryId: entryId));
        await vm.LoadTradesAsyncForTest();

        var buyRow = vm.Trades.First(t => t.Type == TradeType.Buy);
        vm.Transaction.EditTradeCommand.Execute(buyRow);
        await Task.Delay(120);

        Assert.Equal("18.0400", vm.AddAssetDialog.AddPrice);
    }

    [Fact]
    public async Task EditTrade_ZeroCommissionBuy_DoesNotReintroduceAutoFee()
    {
        // WHY: 編輯「無手續費」(Commission=0，如成交總額已含手續費) 的買入時，不該用
        //      「折扣 × 標準費率」重新估一筆手續費把總成本灌大；TxFee 應還原為 "0"。
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(0m);
        var entryId = Guid.NewGuid();
        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: "00919", Exchange: "TWSE", Name: "群益台灣精選高息",
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 21.9814m, Quantity: 20000,
            RealizedPnl: null, RealizedPnlPct: null,
            Commission: 0m, PortfolioEntryId: entryId));
        await vm.LoadTradesAsyncForTest();

        var buyRow = vm.Trades.First(t => t.Type == TradeType.Buy);
        vm.Transaction.EditTradeCommand.Execute(buyRow);

        Assert.Equal("0", vm.Transaction.TxFee);
    }

    [Fact]
    public async Task EditTrade_LegacyBuyWithoutLink_OpensWithEditableEconomicFields()
    {
        // Source-of-truth pass (commit 6836323): legacy Buy trades (no PortfolioEntryId
        // from imports / older versions) are no longer meta-only. Cost basis re-projects
        // from the journal on next reload, so a direct edit is safe — ConfirmTx promotes
        // to implicit revision and the PortfolioEntry is rebuilt from the new Buy.
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(0m);
        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: "2330", Exchange: "TWSE", Name: "TSMC",
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 600, Quantity: 1000,
            RealizedPnl: null, RealizedPnlPct: null,
            PortfolioEntryId: null));
        await vm.LoadTradesAsyncForTest();

        var buyRow = vm.Trades.First(t => t.Type == TradeType.Buy);
        vm.Transaction.EditTradeCommand.Execute(buyRow);

        Assert.False(vm.Transaction.IsEditingMetaOnly);
        Assert.True(vm.Transaction.AreEconomicFieldsEditable);
    }

    // Simplified loan UX: full amount to Balance + cash account

    [Fact]
    public async Task ConfirmTx_LoanRepay_FullAmountDeductsBalanceAndCashAccount()
    {
        // User pays 25,978 from 台新 Richart; Balance -25,978, cash -25,978.
        // No implicit interest splitting.
        var (vm, cashRepo, tradeRepo, _) = await CreateVmWithLiabilityAndCashAsync(
            initialBalance: 2_000_000m, initialCash: 100_000m);

        vm.Transaction.TxType = "loanRepay";
        vm.Transaction.Loan.Principal = "25978";   // Phase 3: full amount as principal (no interest)
        vm.Transaction.Loan.Label = vm.Liabilities.First().Label;
        vm.Transaction.TxCashAccount = vm.CashAccounts.First();
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Empty(vm.Transaction.TxError);
        Assert.Equal(2_000_000m - 25_978m, vm.Liabilities[0].Balance);
        Assert.Equal(100_000m - 25_978m, vm.CashAccounts[0].Balance);

        var trade = tradeRepo.Store.Single(t => t.Type == TradeType.LoanRepay);
        Assert.Equal(25_978m, trade.CashAmount);
        Assert.Equal(vm.CashAccounts[0].Id, trade.CashAccountId);
        // No fee was entered → no extra Withdrawal fee trade should be created.
        Assert.Empty(tradeRepo.Store.Where(t => t.Type == TradeType.Withdrawal));
    }

    [Fact]
    public async Task ConfirmTx_LoanRepay_PrincipalAndInterestPaid_SplitCorrectly()
    {
        // 還款 25,000 本金 + 978 利息 →
        //   LoanRepay.CashAmount   = 25,978（現金扣合計）
        //   LoanRepay.Principal    = 25,000（負債減少）
        //   LoanRepay.InterestPaid = 978  （費用支出，不影響負債）
        var (vm, _, tradeRepo, _) = await CreateVmWithLiabilityAndCashAsync(
            initialBalance: 2_000_000m, initialCash: 100_000m);

        vm.Transaction.TxType = "loanRepay";
        vm.Transaction.Loan.Principal = "25000";
        vm.Transaction.Loan.InterestPaid = "978";
        vm.Transaction.Loan.Label = vm.Liabilities.First().Label;
        vm.Transaction.TxCashAccount = vm.CashAccounts.First();
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Empty(vm.Transaction.TxError);
        // 負債只扣本金（projection-based）
        Assert.Equal(2_000_000m - 25_000m, vm.Liabilities[0].Balance);
        // 現金扣合計（本金 + 利息）
        Assert.Equal(100_000m - 25_978m, vm.CashAccounts[0].Balance);

        var trade = tradeRepo.Store.Single(t => t.Type == TradeType.LoanRepay);
        Assert.Equal(25_978m, trade.CashAmount);   // 合計
        Assert.Equal(25_000m, trade.Principal);    // 本金
        Assert.Equal(978m, trade.InterestPaid); // 利息
    }

    [Fact]
    public async Task ConfirmTx_LoanRepay_WithoutCashAccount_OnlyDeductsBalance()
    {
        // Backwards compatible: cash account is optional.
        var (vm, _, tradeRepo, _) = await CreateVmWithLiabilityAsync(
            initialBalance: 2_000_000m, original: 2_000_000m);

        vm.Transaction.TxType = "loanRepay";
        vm.Transaction.Loan.Principal = "25978";
        vm.Transaction.Loan.Label = vm.Liabilities.First().Label;
        vm.Transaction.TxCashAccount = null;
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Equal(2_000_000m - 25_978m, vm.Liabilities[0].Balance);
        var trade = tradeRepo.Store.Single(t => t.Type == TradeType.LoanRepay);
        Assert.Null(trade.CashAccountId);
    }

    [Fact]
    public async Task ConfirmTx_LoanBorrow_FullAmountAddsBalanceAndCashAccount()
    {
        var (vm, _, _, _) = await CreateVmWithLiabilityAndCashAsync(
            initialBalance: 0m, initialCash: 0m);

        vm.Transaction.TxType = "loanBorrow";
        vm.Transaction.TxAmount = "1000000";
        vm.Transaction.Loan.Label = "台新A 7y";
        vm.Transaction.TxCashAccount = vm.CashAccounts.First();
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Equal(1_000_000m, vm.Liabilities[0].Balance);
        Assert.Equal(1_000_000m, vm.CashAccounts[0].Balance);
    }

    // Loan fee (AscentPortfolio pattern: 手續費 field + checkbox)

    [Fact]
    public async Task ConfirmTx_LoanBorrow_WithFee_CreatesBorrowAndWithdrawalFeePair()
    {
        // 借款 2,000,000、手續費 6,000 → 應建兩筆 trade：
        //   LoanBorrow  CashAmount=2,000,000 (Balance + 2M, Cash + 2M)
        //   Withdrawal  CashAmount=6,000     (Cash - 6,000, 備註含「手續費」)
        // Net cash change: +1,994,000 (等於實際撥款).
        var (vm, _, tradeRepo, _) = await CreateVmWithLiabilityAndCashAsync(0m, 0m);

        vm.Transaction.TxType = "loanBorrow";
        vm.Transaction.TxAmount = "2000000";
        vm.Transaction.TxFee = "6000";
        vm.Transaction.Loan.Label = "台新A 7y";
        vm.Transaction.TxUseCashAccount = true;
        vm.Transaction.TxCashAccount = vm.CashAccounts.First();
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Empty(vm.Transaction.TxError);
        Assert.Equal(2_000_000m, vm.Liabilities[0].Balance);
        Assert.Equal(1_994_000m, vm.CashAccounts[0].Balance);

        var borrows = tradeRepo.Store.Where(t => t.Type == TradeType.LoanBorrow).ToList();
        // The helper seeds a LoanBorrow only when `original > 0`; here both are 0 so this
        // is the user-initiated borrow only.
        var fees = tradeRepo.Store.Where(t => t.Type == TradeType.Withdrawal).ToList();
        Assert.Single(borrows);
        Assert.Single(fees);
        Assert.Equal(2_000_000m, borrows[0].CashAmount);
        // Withdrawal fee stores the absolute amount; PrimaryCashDelta applies the sign.
        Assert.Equal(6_000m, fees[0].CashAmount);
        Assert.Contains("手續費", fees[0].Note ?? "");
    }

    [Fact]
    public async Task ConfirmTx_LoanRepay_WithFee_DeductsAmountPlusFee()
    {
        // 還款 25,978、處理費 100 → LoanRepay -25,978 + Interest -100
        // Net cash change: -26,078.
        var (vm, _, _, _) = await CreateVmWithLiabilityAndCashAsync(
            initialBalance: 2_000_000m, initialCash: 100_000m);

        vm.Transaction.TxType = "loanRepay";
        vm.Transaction.Loan.Principal = "25978";   // Phase 3: full principal, no interest split
        vm.Transaction.TxFee = "100";
        vm.Transaction.Loan.Label = vm.Liabilities.First().Label;
        vm.Transaction.TxUseCashAccount = true;
        vm.Transaction.TxCashAccount = vm.CashAccounts.First();
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Equal(2_000_000m - 25_978m, vm.Liabilities[0].Balance);
        Assert.Equal(100_000m - 26_078m, vm.CashAccounts[0].Balance);
    }

    [Fact]
    public async Task ConfirmTx_Loan_WithoutFee_NoFeeTradeCreated()
    {
        // 手續費空白 → 只建主 trade，不產生多餘的 Withdrawal fee。
        var (vm, _, tradeRepo, _) = await CreateVmWithLiabilityAndCashAsync(0m, 0m);

        vm.Transaction.TxType = "loanBorrow";
        vm.Transaction.TxAmount = "500000";
        vm.Transaction.TxFee = "";
        vm.Transaction.Loan.Label = "台新A 7y";
        vm.Transaction.TxUseCashAccount = true;
        vm.Transaction.TxCashAccount = vm.CashAccounts.First();
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Single(tradeRepo.Store.Where(t => t.Type == TradeType.LoanBorrow));
        Assert.Empty(tradeRepo.Store.Where(t => t.Type == TradeType.Withdrawal));
    }

    [Fact]
    public async Task ConfirmTx_Loan_UseCashAccountUnchecked_SkipsCashEffects()
    {
        // 勾選框關掉 → 即使 TxCashAccount 有值，也不碰現金；Balance 仍然 +amount。
        var (vm, _, tradeRepo, _) = await CreateVmWithLiabilityAndCashAsync(0m, 50_000m);

        vm.Transaction.TxType = "loanBorrow";
        vm.Transaction.TxAmount = "1000000";
        vm.Transaction.TxFee = "3000";
        vm.Transaction.Loan.Label = "台新A 7y";
        vm.Transaction.TxUseCashAccount = false;   // 關掉
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Equal(1_000_000m, vm.Liabilities[0].Balance);
        Assert.Equal(50_000m, vm.CashAccounts[0].Balance);  // 沒動
        // Fee trade 仍然建立，但也沒連動到現金帳戶。
        var feeTrade = tradeRepo.Store.Single(t => t.Type == TradeType.Withdrawal);
        Assert.Null(feeTrade.CashAccountId);
    }

    [Fact]
    public async Task ConfirmTx_Loan_NegativeFee_Rejected()
    {
        var (vm, _, tradeRepo, _) = await CreateVmWithLiabilityAndCashAsync(0m, 0m);

        vm.Transaction.TxType = "loanBorrow";
        vm.Transaction.TxAmount = "1000000";
        vm.Transaction.TxFee = "-500";
        vm.Transaction.Loan.Label = "台新A 7y";
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Contains("手續費", vm.Transaction.TxError);
        Assert.Empty(tradeRepo.Store);  // nothing written
    }

    [Fact]
    public void TxUseCashAccountChanged_UncheckClearsCashAccount()
    {
        // Toggle off should null out the cash account reference so confirms don't
        // accidentally see a stale selection.
        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync([]);
        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);
        var vm = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object),
            new PortfolioServices(SilentStockService().Object, new Mock<IStockSearchService>().Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill)),
            new PortfolioUiServices(ImmediateScheduler.Instance));

        var fakeAcc = new CashAccountRowViewModel(
            new AssetItem(Guid.NewGuid(), "X", FinancialType.Asset, null, "TWD", DateOnly.FromDateTime(DateTime.Today)),
            projectedBalance: 0m);
        vm.Internal_AddCashAccount(fakeAcc);
        vm.Transaction.TxCashAccount = fakeAcc;
        Assert.NotNull(vm.Transaction.TxCashAccount);

        vm.Transaction.TxUseCashAccount = false;
        Assert.Null(vm.Transaction.TxCashAccount);
    }

    // Stock dialog: 單價/總額 toggle, manual fee, CashDiv total mode

    [Fact]
    public async Task TxBuyTotalCost_TotalMode_AutoComputesUnitPrice()
    {
        // 在 total mode 下輸入總額 90,200 + 數量 5,000 → AddPrice 自動回算 18.04
        // （高精度反推、不補尾零；重點是數值正確，避免 90,200/5,000 被 F4 round-trip 改值）
        var (vm, _, _, _) = await CreateVmWithLiabilityAsync(0m, 0m);
        vm.AddAssetDialog.AddQuantity = "5000";
        vm.Transaction.Buy.PriceMode = "total";
        vm.Transaction.Buy.TotalCost = "90200";
        Assert.Equal("18.04", vm.AddAssetDialog.AddPrice);
    }

    [Fact]
    public async Task ConfirmCashDiv_TotalMode_AcceptsTotalInput()
    {
        // 不輸入每股股利、改填總股息 1,000 → 應該成功（per-share 自動回算）
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(0m);
        vm.AddAssetDialog.AddAssetType = "cash";
        vm.AddAssetDialog.AddAccountName = "X";
        await vm.AddAssetDialog.ConfirmAddCommand.ExecuteAsync(null);

        // Need a position to attach dividend to — use simulator: bypass via direct add
        var posRepo = new Mock<IPortfolioRepository>();
        posRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync(
            [new PortfolioEntry(Guid.NewGuid(), "0050", "TWSE")]);
        // For this test simplicity, just verify the validation path: total mode with valid
        // total should not produce "每股股利無效" error.
        vm.Transaction.TxType = "cashDiv";
        vm.Transaction.Div.InputMode = "total";
        vm.Transaction.Div.TotalInput = "1000";
        // No Div.Position set → expect "請選擇股票" not "每股股利無效"
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);
        Assert.Contains("股票", vm.Transaction.TxError);
        Assert.DoesNotContain("每股股利", vm.Transaction.TxError);
    }

    [Fact]
    public async Task ConfirmCashDiv_TotalMode_InvalidTotal_Rejected()
    {
        var (vm, _, _) = await CreateVmWithCashAsync(0m);
        vm.Transaction.TxType = "cashDiv";
        vm.Transaction.Div.InputMode = "total";
        vm.Transaction.Div.TotalInput = "abc";

        // Need position fake to bypass first guard
        var fakePos = new PortfolioRowViewModel
        {
            Id = Guid.NewGuid(),
            Symbol = "0050",
            Quantity = 1000,
            BuyPrice = 100,
        };
        vm.Internal_AddPosition(fakePos);
        vm.Transaction.Div.Position = fakePos;
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Contains("總股息金額無效", vm.Transaction.TxError);
    }

    [Fact]
    public void TxBuyPriceMode_Toggle_FlipsBoolPredicates()
    {
        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync([]);
        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);
        var vm = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object),
            new PortfolioServices(SilentStockService().Object, new Mock<IStockSearchService>().Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill)),
            new PortfolioUiServices(ImmediateScheduler.Instance));

        Assert.True(vm.Transaction.Buy.IsUnitMode);
        Assert.False(vm.Transaction.Buy.IsTotalMode);
        vm.Transaction.Buy.PriceMode = "total";
        Assert.False(vm.Transaction.Buy.IsUnitMode);
        Assert.True(vm.Transaction.Buy.IsTotalMode);
    }

    [Fact]
    public void SwitchingBuyToTotalMode_BackfillsTotalFromPriceTimesQuantity()
    {
        // WHY: 編輯/輸入單價後切到「成交總額」模式，總額欄不該是空的 0.00。
        //      未含手續費時應帶入 單價 × 數量（成交金額），手續費另計、總成本不變。
        var (vm, _) = CreateVm([]);
        vm.Transaction.AddAssetDialog.AddPrice = "21.9814";
        vm.Transaction.AddAssetDialog.AddQuantity = "20000";
        vm.Transaction.Buy.TotalIncludesFee = false;  // 未含手續費 → 回填成交金額(gross)

        vm.Transaction.Buy.PriceMode = "total";

        Assert.Equal("439628", vm.Transaction.Buy.TotalCost);
    }

    [Fact]
    public void TypingBuyTotalCost_IsNotRewrittenByPriceSyncLoop()
    {
        // WHY: 在「成交總額」輸入後，TotalCost↔AddPrice 互相回寫的迴圈會把使用者輸入改掉
        //      （搭配千分位 + 每鍵觸發甚至會放大成另一個數字）。輸入值必須原封不動保留。
        var (vm, _) = CreateVm([]);
        vm.Transaction.AddAssetDialog.AddQuantity = "11000";
        vm.Transaction.Buy.PriceMode = "total";

        vm.Transaction.Buy.TotalCost = "123456789";

        Assert.Equal("123456789", vm.Transaction.Buy.TotalCost);
    }

    [Fact]
    public void BuyTotalMode_PreservesEnteredTotal_NoRoundTripError()
    {
        // WHY: 成交總額 123,456,789 不該被「4 位小數單價 × 數量」round-trip 成 123,456,790。
        //      高精度反推單價後，試算的成交金額/總成本要等於使用者輸入的總額。
        var (vm, _) = CreateVm([]);
        vm.Transaction.AddAssetDialog.AddQuantity = "20000";
        vm.Transaction.Buy.PriceMode = "total";
        vm.Transaction.Buy.TotalIncludesFee = true;   // 已含手續費 → 成交金額 = 總成本 = 輸入總額

        vm.Transaction.Buy.TotalCost = "123456789";

        Assert.Equal(123456789m, vm.Transaction.AddAssetDialog.AddGrossAmount);
        Assert.Equal(123456789m, vm.Transaction.AddAssetDialog.AddTotalCost);
    }

    [Fact]
    public void TxDivInputMode_Toggle_FlipsBoolPredicates()
    {
        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync([]);
        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);
        var vm = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object),
            new PortfolioServices(SilentStockService().Object, new Mock<IStockSearchService>().Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill)),
            new PortfolioUiServices(ImmediateScheduler.Instance));

        Assert.True(vm.Transaction.Div.IsPerShareMode);
        Assert.False(vm.Transaction.Div.IsTotalMode);
        vm.Transaction.Div.InputMode = "total";
        Assert.False(vm.Transaction.Div.IsPerShareMode);
        Assert.True(vm.Transaction.Div.IsTotalMode);
    }

    // Cash-flow fee + Transfer

    [Fact]
    public async Task ConfirmTx_Withdrawal_WithFee_DeductsAmountPlusFee()
    {
        // 提款 5000 + 跨行手續費 15 → 現金少 5015。Single-truth 下兩筆都是 Withdrawal：
        //   (1) 主 Withdrawal：CashAmount=5000
        //   (2) 手續費 Withdrawal：CashAmount=15、Name/Note 標示手續費
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(10_000m);

        vm.Transaction.TxType = "withdrawal";
        vm.Transaction.TxAmount = "5000";
        vm.Transaction.TxFee = "15";
        vm.Transaction.TxCashAccount = vm.CashAccounts.First();
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Empty(vm.Transaction.TxError);
        Assert.Equal(10_000m - 5_015m, vm.CashAccounts[0].Balance);

        var withdrawals = tradeRepo.Store
            .Where(t => t.Type == TradeType.Withdrawal && t.Note != "seed")
            .ToList();
        Assert.Equal(2, withdrawals.Count);
        Assert.Contains(withdrawals, t => t.CashAmount == 5_000m && t.Name != "手續費");
        Assert.Contains(withdrawals, t => t.CashAmount == 15m && t.Name == "手續費");
    }

    [Fact]
    public async Task ConfirmTx_Deposit_WithFee_NetsAmountMinusFee()
    {
        // 存入 1000 + 跨行費 15 → 現金 +985 (= +1000 -15)
        var (vm, _, _) = await CreateVmWithCashAsync(0m);
        vm.AddAssetDialog.AddAssetType = "cash";
        vm.AddAssetDialog.AddAccountName = "X";
        await vm.AddAssetDialog.ConfirmAddCommand.ExecuteAsync(null);

        vm.Transaction.TxType = "deposit";
        vm.Transaction.TxAmount = "1000";
        vm.Transaction.TxFee = "15";
        vm.Transaction.TxCashAccount = vm.CashAccounts.First();
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Equal(985m, vm.CashAccounts[0].Balance);
    }

    [Fact]
    public async Task ConfirmTx_Transfer_SameCurrency_MovesMoneyBetweenAccounts()
    {
        // TWD Savings 30,000 → USD Savings 30,000 (same amount → native Transfer record)
        var (vm, cashRepo, tradeRepo) = await CreateVmWithCashAsync(50_000m);  // 1st account
        vm.AddAssetDialog.AddAssetType = "cash";
        vm.AddAssetDialog.AddAccountName = "USD Savings";
        await vm.AddAssetDialog.ConfirmAddCommand.ExecuteAsync(null);   // 2nd account

        var src = vm.CashAccounts.First();   // 50,000 starting
        var dst = vm.CashAccounts.Last();    // 0 starting

        vm.Transaction.TxType = "transfer";
        vm.Transaction.TxAmount = "30000";
        vm.Transaction.Transfer.TargetAmount = "30000";
        vm.Transaction.TxCashAccount = src;
        vm.Transaction.Transfer.Target = dst;
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Empty(vm.Transaction.TxError);
        Assert.Equal(20_000m, src.Balance);
        Assert.Equal(30_000m, dst.Balance);

        // Same-amount transfer → single native Transfer record (no Withdrawal/Deposit pair).
        var transfer = tradeRepo.Store.Single(t => t.Type == TradeType.Transfer);
        Assert.Equal(30_000m, transfer.CashAmount);
        Assert.Equal(src.Id, transfer.CashAccountId);
        Assert.Equal(dst.Id, transfer.ToCashAccountId);
    }

    [Fact]
    public async Task Transfer_SameCurrency_TypingSourceAmount_AutoFillsTargetAmount()
    {
        // WHY: 同幣別轉帳金額一致，使用者只需輸入一次轉出金額，轉入金額應自動帶入（end-to-end 連動）。
        var (vm, _, _) = await CreateVmWithCashAsync(50_000m);   // 基準帳戶為 TWD
        vm.AddAssetDialog.AddAssetType = "cash";
        vm.AddAssetDialog.AddAccountName = "TWD Second";
        await vm.AddAssetDialog.ConfirmAddCommand.ExecuteAsync(null);   // 第二個帳戶亦為 TWD

        vm.Transaction.TxType = "transfer";
        vm.Transaction.TxCashAccount = vm.CashAccounts.First();
        vm.Transaction.Transfer.Target = vm.CashAccounts.Last();
        vm.Transaction.TxAmount = "200000";

        Assert.Equal("200000", vm.Transaction.Transfer.TargetAmount);
    }

    [Fact]
    public async Task ConfirmTx_Transfer_DifferentAmounts_HandlesFxRate()
    {
        // 30,000 TWD → 1,000 USD (跨幣別)
        var (vm, _, _) = await CreateVmWithCashAsync(50_000m);
        vm.AddAssetDialog.AddAssetType = "cash";
        vm.AddAssetDialog.AddAccountName = "USD Savings";
        await vm.AddAssetDialog.ConfirmAddCommand.ExecuteAsync(null);

        var src = vm.CashAccounts.First();
        var dst = vm.CashAccounts.Last();

        vm.Transaction.TxType = "transfer";
        vm.Transaction.TxAmount = "30000";
        vm.Transaction.Transfer.TargetAmount = "1000";
        vm.Transaction.TxCashAccount = src;
        vm.Transaction.Transfer.Target = dst;
        // Implied rate auto-computed: 30000 / 1000 = 30
        Assert.Equal("30.0000", vm.Transaction.Transfer.ImpliedRate);

        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Equal(20_000m, src.Balance);
        Assert.Equal(1_000m, dst.Balance);
    }

    [Fact]
    public async Task ConfirmTx_Transfer_WithFee_DeductsFromSource()
    {
        // 30,000 → 30,000 with 50 fee → src 50,050 less, dst 30,000 more
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(50_000m);
        vm.AddAssetDialog.AddAssetType = "cash";
        vm.AddAssetDialog.AddAccountName = "X";
        await vm.AddAssetDialog.ConfirmAddCommand.ExecuteAsync(null);

        var src = vm.CashAccounts.First();
        var dst = vm.CashAccounts.Last();

        vm.Transaction.TxType = "transfer";
        vm.Transaction.TxAmount = "30000";
        vm.Transaction.Transfer.TargetAmount = "30000";
        vm.Transaction.TxFee = "50";
        vm.Transaction.TxCashAccount = src;
        vm.Transaction.Transfer.Target = dst;
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Equal(50_000m - 30_050m, src.Balance);
        Assert.Equal(30_000m, dst.Balance);
        // Same-amount transfer is a single Transfer record. The fee is a separate
        // Withdrawal against the source account.
        var fees = tradeRepo.Store
            .Where(t => t.Type == TradeType.Withdrawal && t.Name == "手續費")
            .ToList();
        Assert.Single(fees);
        Assert.Equal(50m, fees[0].CashAmount);
    }

    [Fact]
    public async Task ConfirmTx_Transfer_SameAccountSrcDst_Rejected()
    {
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(10_000m);
        vm.AddAssetDialog.AddAssetType = "cash";
        vm.AddAssetDialog.AddAccountName = "X";
        await vm.AddAssetDialog.ConfirmAddCommand.ExecuteAsync(null);

        var sameAcc = vm.CashAccounts.First();

        vm.Transaction.TxType = "transfer";
        vm.Transaction.TxAmount = "1000";
        vm.Transaction.Transfer.TargetAmount = "1000";
        vm.Transaction.TxCashAccount = sameAcc;
        vm.Transaction.Transfer.Target = sameAcc;
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Contains("同一個", vm.Transaction.TxError);
        // Migration seed Deposit might exist; verify only transfer-tagged trades weren't written.
        Assert.Empty(tradeRepo.Store.Where(t =>
            (t.Type == TradeType.Withdrawal || t.Type == TradeType.Deposit) &&
            (t.Note ?? "").Contains("轉帳")));
    }

    [Fact]
    public async Task ConfirmTx_Transfer_NeedsAtLeastTwoCashAccounts()
    {
        // Only one cash account → reject.
        var (vm, _, _) = await CreateVmWithCashAsync(10_000m);

        vm.Transaction.TxType = "transfer";
        vm.Transaction.TxAmount = "1000";
        vm.Transaction.Transfer.TargetAmount = "1000";
        vm.Transaction.TxCashAccount = vm.CashAccounts.First();
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.Transaction.TxError);
    }

    [Fact]
    public void TxTransferImpliedRateDisplay_NoOrInvalidInput_ReturnsDash()
    {
        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync([]);
        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);
        var vm = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object),
            new PortfolioServices(SilentStockService().Object, new Mock<IStockSearchService>().Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill)),
            new PortfolioUiServices(ImmediateScheduler.Instance));

        Assert.Equal("—", vm.Transaction.Transfer.ImpliedRate);
        vm.Transaction.TxAmount = "100";
        Assert.Equal("—", vm.Transaction.Transfer.ImpliedRate);  // target still empty
        vm.Transaction.Transfer.TargetAmount = "abc";
        Assert.Equal("—", vm.Transaction.Transfer.ImpliedRate);
        vm.Transaction.Transfer.TargetAmount = "25";
        Assert.Equal("4.0000", vm.Transaction.Transfer.ImpliedRate);  // 100 / 25
    }

    // Helper: VM seeded with both a cash account and a liability.
    // The 4th element is the loan label string used in the seeded liability trades.
    private static async Task<(PortfolioViewModel vm, FakeAssetRepo assetRepo, FakeTradeRepo tradeRepo, string loanLabel)>
        CreateVmWithLiabilityAndCashAsync(decimal initialBalance, decimal initialCash)
    {
        const string loanLabel = "台新A 7y";

        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync([]);

        var assetRepo = new FakeAssetRepo();
        var tradeRepo = new FakeTradeRepo();

        var cashAccount = new AssetItem(
            Guid.NewGuid(), "台新 Richart", FinancialType.Asset, null, "TWD",
            DateOnly.FromDateTime(DateTime.Today));
        assetRepo.Store.Add(cashAccount);

        await SeedCashBaselineAsync(tradeRepo, cashAccount.Id, cashAccount.Name, initialCash);
        // Combined helper keeps OriginalAmount == Balance (no partial repayment seeded).
        await SeedLiabilityBaselineAsync(tradeRepo, loanLabel, initialBalance, initialBalance);

        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);
        var txService = new TransactionService(tradeRepo);
        var balanceQuery = new BalanceQueryService(tradeRepo);
        var vm = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object, Trade: tradeRepo, Asset: assetRepo, LoanSchedule: new Mock<ILoanScheduleRepository>().Object),
            new PortfolioServices(SilentStockService().Object, new Mock<IStockSearchService>().Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                TransactionWorkflow: new TransactionWorkflowService(txService),
                BalanceQuery: balanceQuery),
            new PortfolioUiServices(ImmediateScheduler.Instance));
        await vm.LoadAsync();
        return (vm, assetRepo, tradeRepo, loanLabel);
    }

    private static async Task<(PortfolioViewModel vm, FakeAssetRepo assetRepo, FakeTradeRepo tradeRepo, AssetItem card)>
        CreateVmWithCreditCardAndCashAsync(decimal initialCardBalance, decimal initialCash)
    {
        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(r => r.GetEntriesAsync()).ReturnsAsync([]);

        var assetRepo = new FakeAssetRepo();
        var tradeRepo = new FakeTradeRepo();

        var cashAccount = new AssetItem(
            Guid.NewGuid(), "永豐 Richart", FinancialType.Asset, null, "TWD",
            DateOnly.FromDateTime(DateTime.Today));
        var card = new AssetItem(
            Guid.NewGuid(), "玉山 Pi 卡", FinancialType.Liability, null, "TWD",
            DateOnly.FromDateTime(DateTime.Today),
            LiabilitySubtype: LiabilitySubtype.CreditCard,
            BillingDay: 5,
            DueDay: 20,
            CreditLimit: 80_000m,
            IssuerName: "E.SUN");
        assetRepo.Store.Add(cashAccount);
        assetRepo.Store.Add(card);

        await SeedCashBaselineAsync(tradeRepo, cashAccount.Id, cashAccount.Name, initialCash);
        await SeedCreditCardBaselineAsync(tradeRepo, card, initialCardBalance);

        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);
        var txService = new TransactionService(tradeRepo);
        var balanceQuery = new BalanceQueryService(tradeRepo);
        var vm = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object, Trade: tradeRepo, Asset: assetRepo),
            new PortfolioServices(
                SilentStockService().Object,
                new Mock<IStockSearchService>().Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                TransactionWorkflow: new TransactionWorkflowService(txService),
                CreditCardTransaction: new CreditCardTransactionWorkflowService(assetRepo, txService),
                CreditCardMutation: new CreditCardMutationWorkflowService(assetRepo),
                BalanceQuery: balanceQuery),
            new PortfolioUiServices(ImmediateScheduler.Instance));
        await vm.LoadAsync();
        return (vm, assetRepo, tradeRepo, card);
    }

    [Fact]
    public async Task ConfirmTx_SellEdit_UpdatesDateAndNoteInPlace()
    {
        // Meta-only Sell edit: UPDATE the existing row with new date/note, preserving
        // price/quantity/realized-pnl (which can't be recomputed — the lot is gone).
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(0m);
        var sellId = Guid.NewGuid();
        var originalDate = DateTime.UtcNow.AddDays(-30);
        await tradeRepo.AddAsync(new Trade(
            Id: sellId, Symbol: "2330", Exchange: "TWSE", Name: "TSMC",
            Type: TradeType.Sell, TradeDate: originalDate,
            Price: 650m, Quantity: 1000,
            RealizedPnl: 50_000m, RealizedPnlPct: 8.3m,
            PortfolioEntryId: Guid.NewGuid(),
            Note: "原備註"));
        await vm.LoadTradesAsyncForTest();

        vm.Transaction.EditTradeCommand.Execute(vm.Trades.First(t => t.Id == sellId));
        vm.Transaction.TxDate = DateTime.Today.AddDays(-1);  // new date
        vm.Transaction.TxNote = "改過備註";
        await vm.Transaction.ConfirmTxCommand.ExecuteAsync(null);

        var updated = (await tradeRepo.GetAllAsync()).Single(t => t.Id == sellId);
        Assert.Equal("改過備註", updated.Note);
        Assert.Equal(DateTime.Today.AddDays(-1).Date, updated.TradeDate.ToLocalTime().Date);
        // Economic fields intact:
        Assert.Equal(650m, updated.Price);
        Assert.Equal(1000, updated.Quantity);
        Assert.Equal(50_000m, updated.RealizedPnl);
    }

    private sealed class DelayedClosePriceWorkflow(int delayMs, decimal lookupPrice) : IAddAssetWorkflowService
    {
        public IReadOnlyList<StockSearchResult> SearchSymbols(string query, int maxResults = 8) => [];

        public async Task<ClosePriceLookupResult> LookupClosePriceAsync(
            string symbol,
            DateTime buyDate,
            string? exchange = null,
            CancellationToken ct = default)
        {
            await Task.Delay(delayMs, ct);
            return new ClosePriceLookupResult(true, lookupPrice, "test");
        }

        public BuyPreviewResult BuildBuyPreview(BuyPreviewRequest request) =>
            new(
                request.Price * request.Quantity,
                0m,
                request.Price * request.Quantity,
                request.Price);

        public Task<PortfolioEntry> EnsureStockEntryAsync(EnsureStockEntryRequest request, CancellationToken ct = default) =>
            Task.FromResult(new PortfolioEntry(Guid.NewGuid(), request.Symbol, request.Exchange ?? "TWSE"));

        public Task<StockBuyResult> ExecuteStockBuyAsync(StockBuyRequest request, CancellationToken ct = default) =>
            Task.FromResult(new StockBuyResult(
                new PortfolioEntry(Guid.NewGuid(), request.Symbol, request.Exchange ?? "TWSE"),
                0m,
                request.CommissionDiscount,
                request.Price));

        public Task<ManualAssetCreateResult> CreateManualAssetAsync(ManualAssetCreateRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ManualAssetCreateResult(
                new PortfolioEntry(Guid.NewGuid(), request.Symbol, request.Exchange, request.AssetType, request.Name),
                new PositionSnapshot(Guid.NewGuid(), request.Quantity, request.TotalCost, request.UnitPrice, 0m, DateOnly.FromDateTime(DateTime.Today))));

        public string InferExchange(string symbol) => "TWSE";

        public WatchlistSymbolReadiness CheckWatchlistSymbol(string symbol, string? exchange = null) =>
            new(IsResolved: false, UsDirectoryReady: false);
    }

    private sealed class NoopAccountUpsertWorkflow : IAccountUpsertWorkflowService
    {
        public Task<AccountUpsertResult> CreateAsync(CreateAccountRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AccountUpsertResult(
                new AssetItem(Guid.NewGuid(), request.Name, FinancialType.Asset, null, request.Currency, request.CreatedDate)));

        public Task<AccountUpsertResult> UpdateAsync(UpdateAccountRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AccountUpsertResult(
                new AssetItem(request.AccountId, request.Name, FinancialType.Asset, null, request.Currency, request.CreatedDate)));

        public Task<Guid> FindOrCreateAccountAsync(string name, string currency, CancellationToken ct = default) =>
            Task.FromResult(Guid.NewGuid());
    }

    private sealed class NoopCreditCardMutationWorkflow : ICreditCardMutationWorkflowService
    {
        public Task<CreditCardUpsertResult> CreateAsync(CreateCreditCardRequest request, CancellationToken ct = default) =>
            Task.FromResult(new CreditCardUpsertResult(
                new AssetItem(
                    Guid.NewGuid(),
                    request.Name,
                    FinancialType.Liability,
                    null,
                    request.Currency,
                    request.CreatedDate,
                    LiabilitySubtype: LiabilitySubtype.CreditCard,
                    BillingDay: request.BillingDay,
                    DueDay: request.DueDay,
                    CreditLimit: request.CreditLimit,
                    IssuerName: request.IssuerName)));

        public Task<CreditCardUpsertResult> UpdateAsync(UpdateCreditCardRequest request, CancellationToken ct = default) =>
            Task.FromResult(new CreditCardUpsertResult(
                new AssetItem(
                    request.CardId,
                    request.Name,
                    FinancialType.Liability,
                    null,
                    request.Currency,
                    request.CreatedDate,
                    LiabilitySubtype: LiabilitySubtype.CreditCard,
                    BillingDay: request.BillingDay,
                    DueDay: request.DueDay,
                    CreditLimit: request.CreditLimit,
                    IssuerName: request.IssuerName)));
    }
}
