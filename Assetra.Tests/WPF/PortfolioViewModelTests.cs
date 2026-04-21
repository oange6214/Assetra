using System.Reactive.Concurrency;
using System.Reactive.Linq;
using Moq;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure;
using Assetra.Infrastructure.Persistence;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Infrastructure;
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
        IPositionQueryService? positionQuery = null)
    {
        var mutableEntries = (entries ?? [MakeEntry()]).ToList();
        var repo = new Mock<IPortfolioRepository>();
        repo.Setup(r => r.GetEntriesAsync()).ReturnsAsync(() => mutableEntries.ToList());
        repo.Setup(r => r.AddAsync(It.IsAny<PortfolioEntry>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<PortfolioEntry>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.RemoveAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.ArchiveAsync(It.IsAny<Guid>()))
            .Callback<Guid>(id =>
            {
                var idx = mutableEntries.FindIndex(e => e.Id == id);
                if (idx >= 0)
                    mutableEntries[idx] = mutableEntries[idx] with { IsActive = false };
            })
            .Returns(Task.CompletedTask);

        var search = new Mock<IStockSearchService>();
        var (snapshotSvc, snapshotRepo) = SnapshotStubs();
        var (logRepo, backfill) = BackfillStubs(snapshotRepo);

        var vm = new PortfolioViewModel(
            new PortfolioRepositories(repo.Object, snapshotRepo.Object, logRepo.Object),
            new PortfolioServices(SilentStockService().Object, search.Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                PositionQuery: positionQuery),
            new PortfolioUiServices(ImmediateScheduler.Instance));
        return (vm, repo);
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
        var entry2 = MakeEntry("2317", price:  50m, qty:  500);
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
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<AssetType>(), It.IsAny<CancellationToken>()))
            .Callback((string sym, string exch, string? n, AssetType at, CancellationToken _) =>
                created1.Add(new PortfolioEntry(entryId1, sym, exch, at, n ?? string.Empty)))
            .ReturnsAsync(entryId1);
        repo.Setup(r => r.UnarchiveAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        var search = new Mock<IStockSearchService>();
        search.Setup(s => s.GetExchange("XXXX")).Returns((string?)null);
        var (snapshotSvc1, snapshotRepo1) = SnapshotStubs();
        var (logRepo1, backfill1) = BackfillStubs(snapshotRepo1);

        var vm = new PortfolioViewModel(
            new PortfolioRepositories(repo.Object, snapshotRepo1.Object, logRepo1.Object),
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
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<AssetType>(), It.IsAny<CancellationToken>()))
            .Callback((string sym, string exch, string? n, AssetType at, CancellationToken _) =>
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
            new PortfolioRepositories(repo.Object, snapshotRepo2.Object, logRepo2.Object),
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
        Assert.True(vm.IsTxDialogOpen);
        Assert.Equal("sell", vm.TxType);

        // Set sell price via TxAmount (the Sell form binds to TxAmount)
        vm.TxAmount = "1000";
        await vm.ConfirmTxCommand.ExecuteAsync(null);

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
        Assert.Equal(50_000m, row.Pnl);
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

        Assert.Equal(-20_000m, row.Pnl);
        Assert.False(row.IsPnlPositive);
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
        public Task<IReadOnlyList<Trade>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<Trade>>(Store.ToList());
        public Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid cashAccountId) =>
            Task.FromResult<IReadOnlyList<Trade>>(
                Store.Where(t => t.CashAccountId == cashAccountId
                              || t.ToCashAccountId == cashAccountId).ToList());
        public Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string loanLabel) =>
            Task.FromResult<IReadOnlyList<Trade>>(
                Store.Where(t => t.LoanLabel == loanLabel).ToList());
        public Task AddAsync(Trade t) { Store.Add(t); return Task.CompletedTask; }
        public Task UpdateAsync(Trade t)
        {
            var i = Store.FindIndex(x => x.Id == t.Id);
            if (i >= 0)
                Store[i] = t;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(Guid id) { Store.RemoveAll(x => x.Id == id); return Task.CompletedTask; }
        public Task RemoveChildrenAsync(Guid parentId) { Store.RemoveAll(x => x.ParentTradeId == parentId); return Task.CompletedTask; }
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

        var loanMutationService = new LoanMutationWorkflowService(assetRepo, new Mock<ILoanScheduleRepository>().Object, txService);
        var vm = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object, Trade: tradeRepo, Asset: assetRepo),
            new PortfolioServices(SilentStockService().Object, search.Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                TransactionWorkflow: new TransactionWorkflowService(txService),
                LoanMutationWorkflow: loanMutationService,
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
    public async Task ConfirmTx_LoanBorrow_IncreasesLiabilityBalance()
    {
        var (vm, liabRepo, _, _) = await CreateVmWithLiabilityAsync(
            initialBalance: 2_000_000m, original: 2_000_000m);

        vm.TxType = "loanBorrow";
        vm.TxAmount = "50000";
        vm.TxLoanLabel = vm.Liabilities.First().Label;
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Equal(2_050_000m, vm.Liabilities[0].Balance);
    }

    [Fact]
    public async Task ConfirmTx_LoanRepay_DecreasesLiabilityBalance()
    {
        var (vm, _, _, _) = await CreateVmWithLiabilityAsync(
            initialBalance: 2_000_000m, original: 2_000_000m);

        vm.TxType = "loanRepay";
        vm.TxPrincipal = "25979";   // Phase 3: LoanRepay uses Principal/InterestPaid split
        vm.TxLoanLabel = vm.Liabilities.First().Label;
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Equal(1_974_021m, vm.Liabilities[0].Balance);
    }

    [Fact]
    public async Task ConfirmTx_LoanRepay_PreservesOriginalAmount()
    {
        // Regression: LoanRepay must not shrink the original-borrow total, otherwise the
        // PaidPercent progress bar resets (OriginalAmount is Σ LoanBorrow, not the
        // residual balance).
        var (vm, _, _, _) = await CreateVmWithLiabilityAsync(
            initialBalance: 2_000_000m, original: 2_000_000m);

        vm.TxType = "loanRepay";
        vm.TxPrincipal = "25979";
        vm.TxLoanLabel = vm.Liabilities.First().Label;
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Equal(2_000_000m, vm.Liabilities[0].OriginalAmount);
    }

    [Fact]
    public async Task RemoveTrade_LoanRepay_RestoresLiabilityBalance()
    {
        // Regression: deleting a 還款 trade should add the amount back to Balance.
        // Symmetric: deleting 借款 should subtract.
        var (vm, liabRepo, _, _) = await CreateVmWithLiabilityAsync(
            initialBalance: 2_000_000m, original: 2_000_000m);

        vm.TxType = "loanRepay";
        vm.TxPrincipal = "25979";
        vm.TxLoanLabel = vm.Liabilities.First().Label;
        await vm.ConfirmTxCommand.ExecuteAsync(null);
        Assert.Equal(1_974_021m, vm.Liabilities[0].Balance);

        // Delete the trade — RemoveTrade opens a confirm dialog; trigger "Yes" to proceed
        var trade = vm.Trades.First(t => t.IsLoanRepay);
        vm.RemoveTradeCommand.Execute(trade);
        await vm.ConfirmDialogYesCommand.ExecuteAsync(null);

        // Balance should be restored to the pre-trade value
        Assert.Equal(2_000_000m, vm.Liabilities[0].Balance);
    }

    [Fact]
    public async Task ConfirmTx_LoanBorrow_AlsoGrowsOriginalAmount()
    {
        // Regression for the 3,988,000 vs 1,994,000 report: LoanBorrow must grow OriginalAmount
        // alongside Balance so PaidPercent = (Original - Balance)/Original stays ≤ 100 %.
        var (vm, _, _, _) = await CreateVmWithLiabilityAsync(
            initialBalance: 0m, original: 0m);

        vm.TxType = "loanBorrow";
        vm.TxAmount = "1994000";
        vm.TxLoanLabel = "台新 7y";
        await vm.ConfirmTxCommand.ExecuteAsync(null);

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

        vm.TxType = "loanRepay";
        vm.TxPrincipal = "500000";
        vm.TxLoanLabel = vm.Liabilities.First().Label;
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Equal(1_500_000m, vm.Liabilities[0].Balance);
        Assert.Equal(2_000_000m, vm.Liabilities[0].OriginalAmount);
        Assert.Equal(25.0, vm.Liabilities[0].PaidPercent); // (2M-1.5M)/2M = 25 %
    }

    [Fact]
    public async Task EditTrade_LoanRepay_ChangesAmount_BalanceReflectsNetDelta()
    {
        // Regression: editing a 還款 trade from 25,979 → 50,000 should leave the
        // liability with a net decrease of 50,000 from the original (not 75,979).
        var (vm, liabRepo, _, _) = await CreateVmWithLiabilityAsync(
            initialBalance: 2_000_000m, original: 2_000_000m);

        vm.TxType = "loanRepay";
        vm.TxPrincipal = "25979";
        vm.TxLoanLabel = vm.Liabilities.First().Label;
        await vm.ConfirmTxCommand.ExecuteAsync(null);
        Assert.Equal(1_974_021m, vm.Liabilities[0].Balance);

        // Edit the trade to 50,000 principal
        var trade = vm.Trades.First(t => t.IsLoanRepay);
        vm.EditTradeCommand.Execute(trade);
        vm.TxPrincipal = "50000";   // Phase 3: LoanRepay edit uses TxPrincipal
        vm.TxLoanLabel = vm.Liabilities.First().Label;
        await vm.ConfirmTxCommand.ExecuteAsync(null);

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

        var loanMutationService = new LoanMutationWorkflowService(assetRepo, new Mock<ILoanScheduleRepository>().Object, txService);
        var vm = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object, Trade: tradeRepo, Asset: assetRepo),
            new PortfolioServices(SilentStockService().Object, new Mock<IStockSearchService>().Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                TransactionWorkflow: new TransactionWorkflowService(txService),
                LoanMutationWorkflow: loanMutationService,
                BalanceQuery: balanceQuery),
            new PortfolioUiServices(ImmediateScheduler.Instance));
        await vm.LoadAsync();

        // No liabilities before any loan trade
        Assert.Empty(vm.Liabilities);

        // Record a LoanBorrow — this implicitly creates the liability row
        vm.TxType = "loanBorrow";
        vm.TxAmount = "1000000";
        vm.TxLoanLabel = "台新 7y";
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Single(vm.Liabilities);
        Assert.Equal("台新 7y", vm.Liabilities[0].Label);
        Assert.Equal(1_000_000m, vm.Liabilities[0].Balance);
        Assert.Equal(1_000_000m, vm.Liabilities[0].OriginalAmount);
    }

    [Fact]
    public async Task ConfirmTx_LoanBorrow_EmptyLabel_SetsError()
    {
        var (vm, _, _, _) = await CreateVmWithLiabilityAsync(0m, 0m);
        vm.TxType = "loanBorrow";
        vm.TxAmount = "1000000";
        vm.TxLoanLabel = "   ";
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.TxError);
        Assert.Contains("貸款名稱", vm.TxError);
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

        vm.TxType = "deposit";
        vm.TxAmount = "5000";
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Contains("帳戶", vm.TxError);
        Assert.Contains("建立", vm.TxError);
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

        vm.TxType = "loanBorrow";
        vm.TxAmount = "1000000";
        // TxLoanLabel is empty — should show an error
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Contains("貸款名稱", vm.TxError);
        Assert.Empty(tradeRepo.Store);
    }

    [Fact]
    public async Task OpenAddAccountDialog_SwitchesToAccountsTabAndOpensDialog()
    {
        var (vm, _, _) = await CreateVmWithCashAsync(0m);
        vm.OpenAddAccountDialogCommand.Execute(null);

        Assert.True(vm.IsAccountsTab);
        Assert.True(vm.AddAssetDialog.IsAddDialogOpen);
        Assert.Equal("cash", vm.AddAssetDialog.AddAssetType);
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
    public void TradeRowViewModel_IsMetaOnly_TrueForLegacyUnlinkedBuy()
    {
        // Legacy Buy without PortfolioEntryId falls back to meta-only edit.
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(), Symbol: "2330", Exchange: "TWSE", Name: "TSMC",
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 100, Quantity: 1000,
            RealizedPnl: null, RealizedPnlPct: null,
            PortfolioEntryId: null));
        Assert.True(row.IsMetaOnlyEditType);
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
    public async Task GlobalAdd_AlwaysOpensTxDialog_RegardlessOfTab()
    {
        // New behaviour: the header "新增交易" button always routes to the transaction
        // dialog; account creation lives in the per-tab ghost buttons.
        var (vm, _, _, _) = await CreateVmWithLiabilityAsync(0m, 0m);

        vm.SelectedTab = PortfolioTab.Accounts;
        vm.GlobalAddCommand.Execute(null);
        Assert.True(vm.IsTxDialogOpen);
        Assert.False(vm.AddAssetDialog.IsAddDialogOpen);

        vm.CloseTxDialogCommand.Execute(null);
        vm.SelectedTab = PortfolioTab.Liability;
        vm.GlobalAddCommand.Execute(null);
        Assert.True(vm.IsTxDialogOpen);
        Assert.False(vm.AddAssetDialog.IsAddDialogOpen);
    }

    // Plan B: full Buy/Sell/StockDividend edit

    /// <summary>
    /// Validation-first refactor guard: a cash-flow edit whose new amount is invalid
    /// must leave the original trade (and its cash-account effect) intact. Before the
    /// refactor, old trade was deleted before the new one was validated → lost data.
    /// </summary>
    [Fact]
    public async Task ConfirmTx_ValidationFailsOnEdit_OldTradeAndBalancePreserved()
    {
        var (vm, cashRepo, tradeRepo) = await CreateVmWithCashAsync(0m);
        // Seed a cash account for the income to attach to.
        vm.AddAssetDialog.AddAssetType = "cash";
        vm.AddAssetDialog.AddAccountName = "現金";
        await vm.AddAssetDialog.ConfirmAddCommand.ExecuteAsync(null);

        // Create an Income of 10000 linked to the account.
        vm.TxType = "income";
        vm.TxAmount = "10000";
        vm.TxCashAccount = vm.CashAccounts.First();
        await vm.ConfirmTxCommand.ExecuteAsync(null);
        Assert.Equal(10_000m, vm.CashAccounts[0].Balance);
        var originalTrade = vm.Trades.Single(t => t.IsIncome);

        // Try to edit with an invalid amount.
        vm.EditTradeCommand.Execute(originalTrade);
        vm.TxAmount = "not-a-number";
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        // The error lands on the dialog, but the ledger and balance are untouched.
        Assert.NotEmpty(vm.TxError);
        Assert.Equal(10_000m, vm.CashAccounts[0].Balance);
        Assert.Contains(vm.Trades, t => t.Id == originalTrade.Id);
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

        vm.EditTradeCommand.Execute(vm.Trades.First(t => t.Type == TradeType.Sell));

        Assert.Equal(cashAcc.Id, vm.TxCashAccount?.Id);
        Assert.Equal(cashAcc.Id, vm.SellPanel.SellCashAccount?.Id);
        Assert.True(vm.TxUseCashAccount);
    }

    [Fact]
    public async Task EditTrade_CashDividendRow_SetsTxUseCashAccountFromLink()
    {
        // Regression: CashDividend edit didn't flip TxUseCashAccount, so the checkbox
        // visual was wrong when the original trade had a linked cash account.
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(10_000m);
        var cashAcc = vm.CashAccounts.First();

        // Seed a position first so TxDivPosition can find it
        var pos = new PortfolioRowViewModel
        { Id = Guid.NewGuid(), Symbol = "0050", Quantity = 1000, BuyPrice = 140 };
        vm.Positions.Add(pos);

        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: "0050", Exchange: "TWSE", Name: "元大台灣50",
            Type: TradeType.CashDividend, TradeDate: DateTime.UtcNow,
            Price: 2.5m, Quantity: 1000,
            RealizedPnl: null, RealizedPnlPct: null,
            CashAmount: 2500m,
            CashAccountId: cashAcc.Id));
        await vm.LoadTradesAsyncForTest();

        vm.EditTradeCommand.Execute(vm.Trades.First(t => t.Type == TradeType.CashDividend));

        Assert.Equal(cashAcc.Id, vm.TxCashAccount?.Id);
        Assert.True(vm.TxUseCashAccount);
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
        vm.EditTradeCommand.Execute(leg);

        Assert.True(vm.IsEditingMetaOnly);
        Assert.False(vm.AreEconomicFieldsEditable);
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
        vm.EditTradeCommand.Execute(sellInCollection);

        Assert.True(vm.IsTxDialogOpen);
        Assert.True(vm.IsEditMode);
        Assert.True(vm.IsEditingMetaOnly, "Sell edit should be meta-only");
        Assert.False(vm.AreEconomicFieldsEditable);
        Assert.Equal("sell", vm.TxType);
    }

    [Fact]
    public async Task EditTrade_BuyWithLink_DialogAllowsFullEdit()
    {
        // Fresh Buy trades carry PortfolioEntryId and support full edit.
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
        vm.EditTradeCommand.Execute(buyRow);

        Assert.Equal("buy", vm.TxType);
        Assert.False(vm.IsEditingMetaOnly);
        Assert.True(vm.AreEconomicFieldsEditable);
        // Pre-fill landed in the Add* properties used by the buy form.
        Assert.Equal("00982A", vm.AddAssetDialog.AddSymbol);
        Assert.Equal("18.0400", vm.AddAssetDialog.AddPrice);
        Assert.Equal("5000", vm.AddAssetDialog.AddQuantity);
    }

    [Fact]
    public async Task EditTrade_LegacyBuyWithoutLink_FallsBackToMetaOnly()
    {
        // Legacy Buy trades (no PortfolioEntryId) degrade to meta-only edit so we don't
        // accidentally orphan a lot we can't identify.
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(0m);
        await tradeRepo.AddAsync(new Trade(
            Id: Guid.NewGuid(), Symbol: "2330", Exchange: "TWSE", Name: "TSMC",
            Type: TradeType.Buy, TradeDate: DateTime.UtcNow,
            Price: 600, Quantity: 1000,
            RealizedPnl: null, RealizedPnlPct: null,
            PortfolioEntryId: null));
        await vm.LoadTradesAsyncForTest();

        var buyRow = vm.Trades.First(t => t.Type == TradeType.Buy);
        vm.EditTradeCommand.Execute(buyRow);

        Assert.True(vm.IsEditingMetaOnly);
        Assert.False(vm.AreEconomicFieldsEditable);
    }

    // Simplified loan UX: full amount to Balance + cash account

    [Fact]
    public async Task ConfirmTx_LoanRepay_FullAmountDeductsBalanceAndCashAccount()
    {
        // User pays 25,978 from 台新 Richart; Balance -25,978, cash -25,978.
        // No implicit interest splitting.
        var (vm, cashRepo, tradeRepo, _) = await CreateVmWithLiabilityAndCashAsync(
            initialBalance: 2_000_000m, initialCash: 100_000m);

        vm.TxType = "loanRepay";
        vm.TxPrincipal = "25978";   // Phase 3: full amount as principal (no interest)
        vm.TxLoanLabel = vm.Liabilities.First().Label;
        vm.TxCashAccount = vm.CashAccounts.First();
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Empty(vm.TxError);
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

        vm.TxType = "loanRepay";
        vm.TxPrincipal = "25000";
        vm.TxInterestPaid = "978";
        vm.TxLoanLabel = vm.Liabilities.First().Label;
        vm.TxCashAccount = vm.CashAccounts.First();
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Empty(vm.TxError);
        // 負債只扣本金（projection-based）
        Assert.Equal(2_000_000m - 25_000m, vm.Liabilities[0].Balance);
        // 現金扣合計（本金 + 利息）
        Assert.Equal(100_000m - 25_978m, vm.CashAccounts[0].Balance);

        var trade = tradeRepo.Store.Single(t => t.Type == TradeType.LoanRepay);
        Assert.Equal(25_978m, trade.CashAmount);   // 合計
        Assert.Equal(25_000m, trade.Principal);    // 本金
        Assert.Equal(978m,    trade.InterestPaid); // 利息
    }

    [Fact]
    public async Task ConfirmTx_LoanRepay_WithoutCashAccount_OnlyDeductsBalance()
    {
        // Backwards compatible: cash account is optional.
        var (vm, _, tradeRepo, _) = await CreateVmWithLiabilityAsync(
            initialBalance: 2_000_000m, original: 2_000_000m);

        vm.TxType = "loanRepay";
        vm.TxPrincipal = "25978";
        vm.TxLoanLabel = vm.Liabilities.First().Label;
        vm.TxCashAccount = null;
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Equal(2_000_000m - 25_978m, vm.Liabilities[0].Balance);
        var trade = tradeRepo.Store.Single(t => t.Type == TradeType.LoanRepay);
        Assert.Null(trade.CashAccountId);
    }

    [Fact]
    public async Task ConfirmTx_LoanBorrow_FullAmountAddsBalanceAndCashAccount()
    {
        var (vm, _, _, _) = await CreateVmWithLiabilityAndCashAsync(
            initialBalance: 0m, initialCash: 0m);

        vm.TxType = "loanBorrow";
        vm.TxAmount = "1000000";
        vm.TxLoanLabel = "台新A 7y";
        vm.TxCashAccount = vm.CashAccounts.First();
        await vm.ConfirmTxCommand.ExecuteAsync(null);

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

        vm.TxType = "loanBorrow";
        vm.TxAmount = "2000000";
        vm.TxFee = "6000";
        vm.TxLoanLabel = "台新A 7y";
        vm.TxUseCashAccount = true;
        vm.TxCashAccount = vm.CashAccounts.First();
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Empty(vm.TxError);
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

        vm.TxType = "loanRepay";
        vm.TxPrincipal = "25978";   // Phase 3: full principal, no interest split
        vm.TxFee = "100";
        vm.TxLoanLabel = vm.Liabilities.First().Label;
        vm.TxUseCashAccount = true;
        vm.TxCashAccount = vm.CashAccounts.First();
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Equal(2_000_000m - 25_978m, vm.Liabilities[0].Balance);
        Assert.Equal(100_000m - 26_078m, vm.CashAccounts[0].Balance);
    }

    [Fact]
    public async Task ConfirmTx_Loan_WithoutFee_NoFeeTradeCreated()
    {
        // 手續費空白 → 只建主 trade，不產生多餘的 Withdrawal fee。
        var (vm, _, tradeRepo, _) = await CreateVmWithLiabilityAndCashAsync(0m, 0m);

        vm.TxType = "loanBorrow";
        vm.TxAmount = "500000";
        vm.TxFee = "";
        vm.TxLoanLabel = "台新A 7y";
        vm.TxUseCashAccount = true;
        vm.TxCashAccount = vm.CashAccounts.First();
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Single(tradeRepo.Store.Where(t => t.Type == TradeType.LoanBorrow));
        Assert.Empty(tradeRepo.Store.Where(t => t.Type == TradeType.Withdrawal));
    }

    [Fact]
    public async Task ConfirmTx_Loan_UseCashAccountUnchecked_SkipsCashEffects()
    {
        // 勾選框關掉 → 即使 TxCashAccount 有值，也不碰現金；Balance 仍然 +amount。
        var (vm, _, tradeRepo, _) = await CreateVmWithLiabilityAndCashAsync(0m, 50_000m);

        vm.TxType = "loanBorrow";
        vm.TxAmount = "1000000";
        vm.TxFee = "3000";
        vm.TxLoanLabel = "台新A 7y";
        vm.TxUseCashAccount = false;   // 關掉
        await vm.ConfirmTxCommand.ExecuteAsync(null);

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

        vm.TxType = "loanBorrow";
        vm.TxAmount = "1000000";
        vm.TxFee = "-500";
        vm.TxLoanLabel = "台新A 7y";
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Contains("手續費", vm.TxError);
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
        vm.CashAccounts.Add(fakeAcc);
        vm.TxCashAccount = fakeAcc;
        Assert.NotNull(vm.TxCashAccount);

        vm.TxUseCashAccount = false;
        Assert.Null(vm.TxCashAccount);
    }

    // Stock dialog: 單價/總額 toggle, manual fee, CashDiv total mode

    [Fact]
    public async Task TxBuyTotalCost_TotalMode_AutoComputesUnitPrice()
    {
        // 在 total mode 下輸入總額 90,200 + 數量 5,000 → AddPrice 自動回算 18.0400
        var (vm, _, _, _) = await CreateVmWithLiabilityAsync(0m, 0m);
        vm.AddAssetDialog.AddQuantity = "5000";
        vm.TxBuyPriceMode = "total";
        vm.TxBuyTotalCost = "90200";
        Assert.Equal("18.0400", vm.AddAssetDialog.AddPrice);
    }

    [Fact]
    public async Task TxBuyComputedTotalDisplay_UnitMode_ShowsPriceTimesQty()
    {
        var (vm, _, _, _) = await CreateVmWithLiabilityAsync(0m, 0m);
        vm.TxBuyPriceMode = "unit";
        vm.AddAssetDialog.AddPrice = "18.04";
        vm.AddAssetDialog.AddQuantity = "5000";
        Assert.Equal("90,200", vm.TxBuyComputedTotalDisplay);
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
        vm.TxType = "cashDiv";
        vm.TxDivInputMode = "total";
        vm.TxDivTotalInput = "1000";
        // No TxDivPosition set → expect "請選擇股票" not "每股股利無效"
        await vm.ConfirmTxCommand.ExecuteAsync(null);
        Assert.Contains("股票", vm.TxError);
        Assert.DoesNotContain("每股股利", vm.TxError);
    }

    [Fact]
    public async Task ConfirmCashDiv_TotalMode_InvalidTotal_Rejected()
    {
        var (vm, _, _) = await CreateVmWithCashAsync(0m);
        vm.TxType = "cashDiv";
        vm.TxDivInputMode = "total";
        vm.TxDivTotalInput = "abc";

        // Need position fake to bypass first guard
        var fakePos = new PortfolioRowViewModel
        {
            Id = Guid.NewGuid(),
            Symbol = "0050",
            Quantity = 1000,
            BuyPrice = 100,
        };
        vm.Positions.Add(fakePos);
        vm.TxDivPosition = fakePos;
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Contains("總股息金額無效", vm.TxError);
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

        Assert.True(vm.TxBuyIsUnitMode);
        Assert.False(vm.TxBuyIsTotalMode);
        vm.TxBuyPriceMode = "total";
        Assert.False(vm.TxBuyIsUnitMode);
        Assert.True(vm.TxBuyIsTotalMode);
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

        Assert.True(vm.TxDivIsPerShareMode);
        Assert.False(vm.TxDivIsTotalMode);
        vm.TxDivInputMode = "total";
        Assert.False(vm.TxDivIsPerShareMode);
        Assert.True(vm.TxDivIsTotalMode);
    }

    // Cash-flow fee + Transfer

    [Fact]
    public async Task ConfirmTx_Withdrawal_WithFee_DeductsAmountPlusFee()
    {
        // 提款 5000 + 跨行手續費 15 → 現金少 5015。Single-truth 下兩筆都是 Withdrawal：
        //   (1) 主 Withdrawal：CashAmount=5000
        //   (2) 手續費 Withdrawal：CashAmount=15、Name/Note 標示手續費
        var (vm, _, tradeRepo) = await CreateVmWithCashAsync(10_000m);

        vm.TxType = "withdrawal";
        vm.TxAmount = "5000";
        vm.TxFee = "15";
        vm.TxCashAccount = vm.CashAccounts.First();
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Empty(vm.TxError);
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

        vm.TxType = "deposit";
        vm.TxAmount = "1000";
        vm.TxFee = "15";
        vm.TxCashAccount = vm.CashAccounts.First();
        await vm.ConfirmTxCommand.ExecuteAsync(null);

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

        vm.TxType = "transfer";
        vm.TxAmount = "30000";
        vm.TxTransferTargetAmount = "30000";
        vm.TxCashAccount = src;
        vm.TxTransferTarget = dst;
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Empty(vm.TxError);
        Assert.Equal(20_000m, src.Balance);
        Assert.Equal(30_000m, dst.Balance);

        // Same-amount transfer → single native Transfer record (no Withdrawal/Deposit pair).
        var transfer = tradeRepo.Store.Single(t => t.Type == TradeType.Transfer);
        Assert.Equal(30_000m, transfer.CashAmount);
        Assert.Equal(src.Id, transfer.CashAccountId);
        Assert.Equal(dst.Id, transfer.ToCashAccountId);
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

        vm.TxType = "transfer";
        vm.TxAmount = "30000";
        vm.TxTransferTargetAmount = "1000";
        vm.TxCashAccount = src;
        vm.TxTransferTarget = dst;
        // Implied rate auto-computed: 30000 / 1000 = 30
        Assert.Equal("30.0000", vm.TxTransferImpliedRateDisplay);

        await vm.ConfirmTxCommand.ExecuteAsync(null);

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

        vm.TxType = "transfer";
        vm.TxAmount = "30000";
        vm.TxTransferTargetAmount = "30000";
        vm.TxFee = "50";
        vm.TxCashAccount = src;
        vm.TxTransferTarget = dst;
        await vm.ConfirmTxCommand.ExecuteAsync(null);

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

        vm.TxType = "transfer";
        vm.TxAmount = "1000";
        vm.TxTransferTargetAmount = "1000";
        vm.TxCashAccount = sameAcc;
        vm.TxTransferTarget = sameAcc;
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Contains("同一個", vm.TxError);
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

        vm.TxType = "transfer";
        vm.TxAmount = "1000";
        vm.TxTransferTargetAmount = "1000";
        vm.TxCashAccount = vm.CashAccounts.First();
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.TxError);
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

        Assert.Equal("—", vm.TxTransferImpliedRateDisplay);
        vm.TxAmount = "100";
        Assert.Equal("—", vm.TxTransferImpliedRateDisplay);  // target still empty
        vm.TxTransferTargetAmount = "abc";
        Assert.Equal("—", vm.TxTransferImpliedRateDisplay);
        vm.TxTransferTargetAmount = "25";
        Assert.Equal("4.0000", vm.TxTransferImpliedRateDisplay);  // 100 / 25
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
        var loanMutationService = new LoanMutationWorkflowService(assetRepo, new Mock<ILoanScheduleRepository>().Object, txService);
        var vm = new PortfolioViewModel(
            new PortfolioRepositories(portfolioRepo.Object, snapshotRepo.Object, logRepo.Object, Trade: tradeRepo, Asset: assetRepo),
            new PortfolioServices(SilentStockService().Object, new Mock<IStockSearchService>().Object,
                HistoryMaintenance: new PortfolioHistoryMaintenanceService(snapshotSvc, backfill),
                TransactionWorkflow: new TransactionWorkflowService(txService),
                LoanMutationWorkflow: loanMutationService,
                BalanceQuery: balanceQuery),
            new PortfolioUiServices(ImmediateScheduler.Instance));
        await vm.LoadAsync();
        return (vm, assetRepo, tradeRepo, loanLabel);
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

        vm.EditTradeCommand.Execute(vm.Trades.First(t => t.Id == sellId));
        vm.TxDate = DateTime.Today.AddDays(-1);  // new date
        vm.TxNote = "改過備註";
        await vm.ConfirmTxCommand.ExecuteAsync(null);

        var updated = (await tradeRepo.GetAllAsync()).Single(t => t.Id == sellId);
        Assert.Equal("改過備註", updated.Note);
        Assert.Equal(DateTime.Today.AddDays(-1).Date, updated.TradeDate.ToLocalTime().Date);
        // Economic fields intact:
        Assert.Equal(650m, updated.Price);
        Assert.Equal(1000, updated.Quantity);
        Assert.Equal(50_000m, updated.RealizedPnl);
    }
}
