using System.IO;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Persistence;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public sealed class TradeSqliteRepositorySyncTests : IDisposable
{
    private readonly string _dbPath;
    private readonly FixedTime _time = new(DateTimeOffset.Parse("2026-04-28T00:00:00Z"));

    public TradeSqliteRepositorySyncTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"assetra-trade-sync-{Guid.NewGuid():N}.db");
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { /* best effort */ } }

    private TradeSqliteRepository New(string device = "device-A")
        => new(_dbPath, device, _time);

    private static Trade SampleTrade(Guid? id = null) => new(
        Id: id ?? Guid.NewGuid(),
        Symbol: "2330",
        Exchange: "TWSE",
        Name: "台積電",
        Type: TradeType.Buy,
        TradeDate: DateTime.Parse("2026-04-01T00:00:00Z").ToUniversalTime(),
        Price: 800m,
        Quantity: 1000,
        RealizedPnl: null,
        RealizedPnlPct: null);

    [Fact]
    public async Task Add_StampsVersion1AndPendingPush()
    {
        var repo = New();
        var t = SampleTrade();
        await repo.AddAsync(t);

        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.Equal(t.Id, env.EntityId);
        Assert.Equal("Trade", env.EntityType);
        Assert.Equal(1, env.Version.Version);
        Assert.Equal("device-A", env.Version.LastModifiedByDevice);
        Assert.False(env.Deleted);
    }

    [Fact]
    public async Task Update_BumpsVersion()
    {
        var repo = New();
        var t = SampleTrade();
        await repo.AddAsync(t);
        await repo.UpdateAsync(t with { Price = 850m });

        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.Equal(2, env.Version.Version);
        Assert.Contains("850", env.PayloadJson);
    }

    [Fact]
    public async Task Remove_BecomesTombstone()
    {
        var repo = New();
        var t = SampleTrade();
        await repo.AddAsync(t);
        await repo.RemoveAsync(t.Id);

        Assert.Empty(await repo.GetAllAsync());
        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.True(env.Deleted);
        Assert.Equal(string.Empty, env.PayloadJson);
        Assert.Equal(2, env.Version.Version);
    }

    [Fact]
    public async Task Remove_DetachesChildren()
    {
        var repo = New();
        var parent = SampleTrade();
        var child = SampleTrade() with { ParentTradeId = parent.Id, Symbol = "FEE", Name = "fee" };
        await repo.AddAsync(parent);
        await repo.AddAsync(child);

        await repo.RemoveAsync(parent.Id);

        var stored = await repo.GetByIdAsync(child.Id);
        Assert.NotNull(stored);
        Assert.Null(stored!.ParentTradeId);
    }

    [Fact]
    public async Task MarkPushed_ClearsPendingFlag()
    {
        var repo = New();
        var t = SampleTrade();
        await repo.AddAsync(t);

        await repo.MarkPushedAsync(new[] { t.Id });

        Assert.Empty(await repo.GetPendingPushAsync());
        Assert.Single(await repo.GetAllAsync());
    }

    [Fact]
    public async Task ApplyRemote_InsertsNewEntity()
    {
        var repo = New();
        var t = SampleTrade();
        var env = TradeSyncMapper.ToEnvelope(
            t,
            new EntityVersion(5, _time.GetUtcNow(), "device-B"),
            isDeleted: false);

        await repo.ApplyRemoteAsync(new[] { env });

        var stored = await repo.GetByIdAsync(t.Id);
        Assert.NotNull(stored);
        Assert.Equal("2330", stored!.Symbol);
        Assert.Equal(800m, stored.Price);
        Assert.Empty(await repo.GetPendingPushAsync());
    }

    [Fact]
    public async Task ApplyRemote_DoesNotWriteBackwards()
    {
        var repo = New();
        var t = SampleTrade();
        await repo.AddAsync(t);
        await repo.UpdateAsync(t with { Price = 900m });

        var stale = TradeSyncMapper.ToEnvelope(
            t with { Price = 700m },
            new EntityVersion(1, _time.GetUtcNow().AddDays(1), "device-B"),
            isDeleted: false);

        await repo.ApplyRemoteAsync(new[] { stale });

        var stored = await repo.GetByIdAsync(t.Id);
        Assert.Equal(900m, stored!.Price);
    }

    [Fact]
    public async Task ApplyRemote_TombstoneDeletesLocally()
    {
        var repo = New();
        var t = SampleTrade();
        await repo.AddAsync(t);

        var tombstone = new SyncEnvelope(
            t.Id, "Trade", string.Empty,
            new EntityVersion(99, _time.GetUtcNow(), "device-B"),
            Deleted: true);

        await repo.ApplyRemoteAsync(new[] { tombstone });

        Assert.Null(await repo.GetByIdAsync(t.Id));
    }

    [Fact]
    public async Task ApplyRemote_TombstoneForUnknownId_StoresAsHidden()
    {
        var repo = New();
        var unknown = Guid.NewGuid();
        var tombstone = new SyncEnvelope(
            unknown, "Trade", string.Empty,
            new EntityVersion(1, _time.GetUtcNow(), "device-B"),
            Deleted: true);

        await repo.ApplyRemoteAsync(new[] { tombstone });

        Assert.Null(await repo.GetByIdAsync(unknown));
        Assert.Empty(await repo.GetPendingPushAsync());
    }

    [Fact]
    public async Task RemoveChildrenAsync_SoftDeletesChildrenAndQueuesPush()
    {
        var repo = New();
        var parent = SampleTrade();
        var child1 = SampleTrade() with { ParentTradeId = parent.Id, Symbol = "FEE", Name = "fee1" };
        var child2 = SampleTrade() with { ParentTradeId = parent.Id, Symbol = "FEE", Name = "fee2" };
        await repo.AddAsync(parent);
        await repo.AddAsync(child1);
        await repo.AddAsync(child2);
        await repo.MarkPushedAsync(new[] { parent.Id, child1.Id, child2.Id });

        await repo.RemoveChildrenAsync(parent.Id);

        Assert.Null(await repo.GetByIdAsync(child1.Id));
        Assert.Null(await repo.GetByIdAsync(child2.Id));
        var pending = await repo.GetPendingPushAsync();
        Assert.Equal(2, pending.Count);
        Assert.All(pending, e => Assert.True(e.Deleted));
    }

    [Fact]
    public async Task RemoveByAccountIdAsync_SoftDeletesMatchingTrades()
    {
        var repo = New();
        var acct = Guid.NewGuid();
        var t1 = SampleTrade() with { CashAccountId = acct };
        var t2 = SampleTrade() with { ToCashAccountId = acct, Type = TradeType.Transfer };
        var t3 = SampleTrade(); // unrelated
        await repo.AddAsync(t1);
        await repo.AddAsync(t2);
        await repo.AddAsync(t3);
        await repo.MarkPushedAsync(new[] { t1.Id, t2.Id, t3.Id });

        await repo.RemoveByAccountIdAsync(acct);

        Assert.Null(await repo.GetByIdAsync(t1.Id));
        Assert.Null(await repo.GetByIdAsync(t2.Id));
        Assert.NotNull(await repo.GetByIdAsync(t3.Id));
        var pending = await repo.GetPendingPushAsync();
        Assert.Equal(2, pending.Count);
        Assert.All(pending, e => Assert.True(e.Deleted));
    }

    [Fact]
    public async Task RemoveByLiabilityAsync_SoftDeletesByLoanLabel()
    {
        var repo = New();
        var t1 = SampleTrade() with { LoanLabel = "房貸A" };
        var t2 = SampleTrade(); // unrelated
        await repo.AddAsync(t1);
        await repo.AddAsync(t2);
        await repo.MarkPushedAsync(new[] { t1.Id, t2.Id });

        await repo.RemoveByLiabilityAsync(liabilityAssetId: null, loanLabel: "房貸A");

        Assert.Null(await repo.GetByIdAsync(t1.Id));
        Assert.NotNull(await repo.GetByIdAsync(t2.Id));
        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.True(env.Deleted);
        Assert.Equal(t1.Id, env.EntityId);
    }

    [Fact]
    public async Task GetAll_FiltersOutTombstones()
    {
        var repo = New();
        var keep = SampleTrade();
        var drop = SampleTrade();
        await repo.AddAsync(keep);
        await repo.AddAsync(drop);
        await repo.RemoveAsync(drop.Id);

        var all = await repo.GetAllAsync();
        Assert.Single(all);
        Assert.Equal(keep.Id, all[0].Id);
    }

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTime(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
