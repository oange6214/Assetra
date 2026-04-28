using System.IO;
using Assetra.Application.Sync;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Persistence;
using Assetra.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Integration.Sync;

/// <summary>
/// End-to-end 同步整合測試（v0.20.12）：
/// 把 v0.20.4–v0.20.11 累積的 8 個 entity 類型（Category / Trade / Asset / AssetGroup /
/// AssetEvent / Portfolio / AutoCategorizationRule / RecurringTransaction）以**真實**
/// SQLite repository + <see cref="CompositeLocalChangeQueue"/> + <see cref="SyncOrchestrator"/>
/// + <see cref="InMemoryCloudSyncProvider"/> 串成 round-trip：
/// device-A push 8 筆 → cloud 接收 → device-B pull → device-B 各 repo 看得到。
/// </summary>
public sealed class SyncEndToEndIntegrationTests : IDisposable
{
    private readonly string _dbA;
    private readonly string _dbB;
    private readonly FixedTime _time = new(DateTimeOffset.Parse("2026-04-28T00:00:00Z"));

    public SyncEndToEndIntegrationTests()
    {
        _dbA = Path.Combine(Path.GetTempPath(), $"assetra-e2e-A-{Guid.NewGuid():N}.db");
        _dbB = Path.Combine(Path.GetTempPath(), $"assetra-e2e-B-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbA); } catch { }
        try { File.Delete(_dbB); } catch { }
    }

    [Fact]
    public async Task RoundTrip_AllEightEntityTypes_DeviceAToDeviceB()
    {
        var cloud = new InMemoryCloudSyncProvider();
        var resolver = new LastWriteWinsResolver();

        // ── device-A: seed one entity per type, then push ─────────────────
        var siteA = new SyncSite(_dbA, "device-A", _time, cloud, resolver);

        var category = new ExpenseCategory(Guid.NewGuid(), "餐飲", CategoryKind.Expense);
        var trade = new Trade(
            Id: Guid.NewGuid(), Symbol: "2330", Exchange: "TWSE", Name: "台積電",
            Type: TradeType.Buy, TradeDate: _time.GetUtcNow().UtcDateTime,
            Price: 800m, Quantity: 1000, RealizedPnl: null, RealizedPnlPct: null);
        var asset = new AssetItem(
            Id: Guid.NewGuid(), Name: "玉山活存", Type: FinancialType.Asset,
            GroupId: null, Currency: "TWD", CreatedDate: new DateOnly(2026, 1, 1));
        var group = new AssetGroup(
            Id: Guid.NewGuid(), Name: "我的銀行", Type: FinancialType.Asset,
            Icon: "🏦", SortOrder: 10, IsSystem: false,
            CreatedDate: new DateOnly(2026, 1, 1));
        var assetEvent = new AssetEvent(
            Id: Guid.NewGuid(), AssetId: asset.Id,
            EventType: AssetEventType.Valuation,
            EventDate: _time.GetUtcNow().UtcDateTime,
            Amount: 100000m, Quantity: null, Note: "估值", CashAccountId: null,
            CreatedAt: _time.GetUtcNow().UtcDateTime);
        var portfolio = new PortfolioEntry(
            Id: Guid.NewGuid(), Symbol: "0050", Exchange: "TWSE",
            AssetType: AssetType.Etf, DisplayName: "元大台灣50",
            Currency: "TWD", IsActive: true, IsEtf: true);
        var rule = new AutoCategorizationRule(
            Id: Guid.NewGuid(), KeywordPattern: "全聯", CategoryId: category.Id,
            Priority: 5, Name: "雜貨");
        var recurring = new RecurringTransaction(
            Id: Guid.NewGuid(), Name: "房租",
            TradeType: TradeType.Withdrawal, Amount: 12000m,
            CashAccountId: null, CategoryId: null,
            Frequency: RecurrenceFrequency.Monthly, Interval: 1,
            StartDate: _time.GetUtcNow().UtcDateTime, EndDate: null,
            GenerationMode: AutoGenerationMode.AutoApply);

        await siteA.Categories.AddAsync(category);
        await siteA.Trades.AddAsync(trade);
        await siteA.Assets.AddItemAsync(asset);
        await siteA.Assets.AddGroupAsync(group);
        await siteA.Assets.AddEventAsync(assetEvent);
        await siteA.Portfolios.AddAsync(portfolio);
        await siteA.Rules.AddAsync(rule);
        await siteA.Recurring.AddAsync(recurring);

        var pushResult = await siteA.Orchestrator.SyncAsync();
        Assert.Equal(8, pushResult.PushedCount);
        Assert.Equal(0, pushResult.PulledCount);
        Assert.Equal(0, pushResult.ManualConflicts);

        // ── device-B: pull and verify each entity surfaced ───────────────
        var siteB = new SyncSite(_dbB, "device-B", _time, cloud, resolver);

        var pullResult = await siteB.Orchestrator.SyncAsync();
        Assert.Equal(8, pullResult.PulledCount);
        Assert.Equal(0, pullResult.PushedCount);

        Assert.NotNull(await siteB.Categories.GetByIdAsync(category.Id));
        Assert.NotNull(await siteB.Trades.GetByIdAsync(trade.Id));
        Assert.NotNull(await siteB.Assets.GetByIdAsync(asset.Id));
        var groupB = (await siteB.Assets.GetGroupsAsync()).FirstOrDefault(x => x.Id == group.Id);
        Assert.NotNull(groupB);
        var eventsB = await siteB.Assets.GetEventsAsync(asset.Id);
        Assert.Contains(eventsB, e => e.Id == assetEvent.Id);
        var portfoliosB = await siteB.Portfolios.GetEntriesAsync();
        Assert.Contains(portfoliosB, p => p.Id == portfolio.Id);
        Assert.NotNull(await siteB.Rules.GetByIdAsync(rule.Id));
        Assert.NotNull(await siteB.Recurring.GetByIdAsync(recurring.Id));

        // Pulled-side must NOT mark imports as pending push (no echo back).
        var stillPendingB = await siteB.Composite.GetPendingAsync();
        Assert.Empty(stillPendingB);
    }

    [Fact]
    public async Task RoundTrip_TombstoneFromDeviceA_DeletesOnDeviceB()
    {
        var cloud = new InMemoryCloudSyncProvider();
        var resolver = new LastWriteWinsResolver();
        var siteA = new SyncSite(_dbA, "device-A", _time, cloud, resolver);
        var siteB = new SyncSite(_dbB, "device-B", _time, cloud, resolver);

        var c = new ExpenseCategory(Guid.NewGuid(), "to-drop", CategoryKind.Expense);
        await siteA.Categories.AddAsync(c);
        await siteA.Orchestrator.SyncAsync();
        await siteB.Orchestrator.SyncAsync();
        Assert.NotNull(await siteB.Categories.GetByIdAsync(c.Id));

        await siteA.Categories.RemoveAsync(c.Id);
        await siteA.Orchestrator.SyncAsync();
        await siteB.Orchestrator.SyncAsync();

        Assert.Null(await siteB.Categories.GetByIdAsync(c.Id));
    }

    private sealed class SyncSite
    {
        public CategorySqliteRepository Categories { get; }
        public TradeSqliteRepository Trades { get; }
        public AssetSqliteRepository Assets { get; }
        public PortfolioSqliteRepository Portfolios { get; }
        public AutoCategorizationRuleSqliteRepository Rules { get; }
        public RecurringTransactionSqliteRepository Recurring { get; }
        public CompositeLocalChangeQueue Composite { get; }
        public SyncOrchestrator Orchestrator { get; }

        public SyncSite(
            string dbPath,
            string device,
            TimeProvider time,
            ICloudSyncProvider cloud,
            IConflictResolver resolver)
        {
            Categories = new CategorySqliteRepository(dbPath, device, time);
            Trades = new TradeSqliteRepository(dbPath, device, time);
            Assets = new AssetSqliteRepository(dbPath, device, time);
            Portfolios = new PortfolioSqliteRepository(dbPath, device, time);
            Rules = new AutoCategorizationRuleSqliteRepository(dbPath, device, time);
            Recurring = new RecurringTransactionSqliteRepository(dbPath, device, time);

            var map = new Dictionary<string, ILocalChangeQueue>(StringComparer.Ordinal)
            {
                [CategorySyncMapper.EntityType] = new CategoryLocalChangeQueue(Categories),
                [TradeSyncMapper.EntityType] = new TradeLocalChangeQueue(Trades),
                [AssetSyncMapper.EntityType] = new AssetLocalChangeQueue(Assets),
                [AssetGroupSyncMapper.EntityType] = new AssetGroupLocalChangeQueue(Assets),
                [AssetEventSyncMapper.EntityType] = new AssetEventLocalChangeQueue(Assets),
                [PortfolioSyncMapper.EntityType] = new PortfolioLocalChangeQueue(Portfolios),
                [AutoCategorizationRuleSyncMapper.EntityType] = new AutoCategorizationRuleLocalChangeQueue(Rules),
                [RecurringTransactionSyncMapper.EntityType] = new RecurringTransactionLocalChangeQueue(Recurring),
            };
            Composite = new CompositeLocalChangeQueue(map);

            var meta = new InMemorySyncMetadataStore(device);
            Orchestrator = new SyncOrchestrator(cloud, Composite, meta, resolver, time);
        }
    }

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTime(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
