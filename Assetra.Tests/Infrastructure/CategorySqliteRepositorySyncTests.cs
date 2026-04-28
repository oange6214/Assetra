using System.IO;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Persistence;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public sealed class CategorySqliteRepositorySyncTests : IDisposable
{
    private readonly string _dbPath;
    private readonly FixedTime _time = new(DateTimeOffset.Parse("2026-04-28T00:00:00Z"));

    public CategorySqliteRepositorySyncTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"assetra-cat-sync-{Guid.NewGuid():N}.db");
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { /* best effort */ } }

    private CategorySqliteRepository New(string device = "device-A")
        => new(_dbPath, device, _time);

    [Fact]
    public async Task Add_StampsVersion1AndPendingPush()
    {
        var repo = New();
        var c = new ExpenseCategory(Guid.NewGuid(), "餐飲", CategoryKind.Expense);
        await repo.AddAsync(c);

        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.Equal(c.Id, env.EntityId);
        Assert.Equal(1, env.Version.Version);
        Assert.Equal("device-A", env.Version.LastModifiedByDevice);
        Assert.False(env.Deleted);
    }

    [Fact]
    public async Task Update_BumpsVersion()
    {
        var repo = New();
        var c = new ExpenseCategory(Guid.NewGuid(), "餐飲", CategoryKind.Expense);
        await repo.AddAsync(c);
        await repo.UpdateAsync(c with { Name = "餐飲(2)" });

        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.Equal(2, env.Version.Version);
        Assert.Contains("餐飲(2)", env.PayloadJson);
    }

    [Fact]
    public async Task Remove_BecomesTombstone()
    {
        var repo = New();
        var c = new ExpenseCategory(Guid.NewGuid(), "x", CategoryKind.Expense);
        await repo.AddAsync(c);
        await repo.RemoveAsync(c.Id);

        Assert.Empty(await repo.GetAllAsync()); // user-facing view hides tombstones
        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.True(env.Deleted);
        Assert.Equal(string.Empty, env.PayloadJson);
        Assert.Equal(2, env.Version.Version);
    }

    [Fact]
    public async Task MarkPushed_ClearsPendingFlag()
    {
        var repo = New();
        var c = new ExpenseCategory(Guid.NewGuid(), "x", CategoryKind.Expense);
        await repo.AddAsync(c);

        await repo.MarkPushedAsync(new[] { c.Id });

        Assert.Empty(await repo.GetPendingPushAsync());
        // Entity is still visible in user-facing view
        Assert.Single(await repo.GetAllAsync());
    }

    [Fact]
    public async Task ApplyRemote_InsertsNewEntity()
    {
        var repo = New();
        var id = Guid.NewGuid();
        var remote = new ExpenseCategory(id, "from-remote", CategoryKind.Income, ColorHex: "#0099FF");
        var env = CategorySyncMapper.ToEnvelope(
            remote,
            new EntityVersion(5, _time.GetUtcNow(), "device-B"),
            isDeleted: false);

        await repo.ApplyRemoteAsync(new[] { env });

        var stored = await repo.GetByIdAsync(id);
        Assert.NotNull(stored);
        Assert.Equal("from-remote", stored!.Name);
        Assert.Equal(CategoryKind.Income, stored.Kind);
        // Remote-origin must NOT be re-pushed
        Assert.Empty(await repo.GetPendingPushAsync());
    }

    [Fact]
    public async Task ApplyRemote_DoesNotWriteBackwards()
    {
        var repo = New();
        var c = new ExpenseCategory(Guid.NewGuid(), "local-name", CategoryKind.Expense);
        await repo.AddAsync(c); // version 1
        await repo.UpdateAsync(c with { Name = "local-v2" }); // version 2

        var stale = CategorySyncMapper.ToEnvelope(
            c with { Name = "remote-stale" },
            new EntityVersion(1, _time.GetUtcNow().AddDays(1), "device-B"),
            isDeleted: false);

        await repo.ApplyRemoteAsync(new[] { stale });

        var stored = await repo.GetByIdAsync(c.Id);
        Assert.Equal("local-v2", stored!.Name);
    }

    [Fact]
    public async Task ApplyRemote_TombstoneDeletesLocally()
    {
        var repo = New();
        var c = new ExpenseCategory(Guid.NewGuid(), "to-delete", CategoryKind.Expense);
        await repo.AddAsync(c);

        var tombstone = new SyncEnvelope(
            EntityId: c.Id,
            EntityType: "Category",
            PayloadJson: string.Empty,
            Version: new EntityVersion(99, _time.GetUtcNow(), "device-B"),
            Deleted: true);

        await repo.ApplyRemoteAsync(new[] { tombstone });

        Assert.Null(await repo.GetByIdAsync(c.Id));
    }

    [Fact]
    public async Task ApplyRemote_TombstoneForUnknownId_StoresAsHidden()
    {
        var repo = New();
        var unknown = Guid.NewGuid();
        var tombstone = new SyncEnvelope(
            unknown, "Category", string.Empty,
            new EntityVersion(1, _time.GetUtcNow(), "device-B"),
            Deleted: true);

        await repo.ApplyRemoteAsync(new[] { tombstone });

        Assert.Null(await repo.GetByIdAsync(unknown));
        Assert.Empty(await repo.GetPendingPushAsync());
    }

    [Fact]
    public async Task GetAll_FiltersOutTombstones()
    {
        var repo = New();
        var keep = new ExpenseCategory(Guid.NewGuid(), "keep", CategoryKind.Expense);
        var drop = new ExpenseCategory(Guid.NewGuid(), "drop", CategoryKind.Expense);
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
