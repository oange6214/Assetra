using System.IO;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Persistence;
using Assetra.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public sealed class AssetSqliteRepositorySyncTests : IDisposable
{
    private readonly string _dbPath;
    private readonly FixedTime _time = new(DateTimeOffset.Parse("2026-04-28T00:00:00Z"));

    public AssetSqliteRepositorySyncTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"assetra-asset-sync-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private AssetSqliteRepository New(string device = "device-A")
        => new(_dbPath, device, _time);

    private static AssetItem SampleItem(Guid? id = null) => new(
        Id: id ?? Guid.NewGuid(),
        Name: "玉山活存",
        Type: FinancialType.Asset,
        GroupId: null,
        Currency: "TWD",
        CreatedDate: new DateOnly(2026, 1, 1));

    [Fact]
    public async Task Add_StampsVersion1AndPendingPush()
    {
        var repo = New();
        var item = SampleItem();
        await repo.AddItemAsync(item);

        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.Equal(item.Id, env.EntityId);
        Assert.Equal("Asset", env.EntityType);
        Assert.Equal(1, env.Version.Version);
        Assert.Equal("device-A", env.Version.LastModifiedByDevice);
        Assert.False(env.Deleted);
    }

    [Fact]
    public async Task Update_BumpsVersion()
    {
        var repo = New();
        var item = SampleItem();
        await repo.AddItemAsync(item);
        await repo.UpdateItemAsync(item with { Name = "改名後" });

        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.Equal(2, env.Version.Version);
        Assert.Contains("改名後", env.PayloadJson);
    }

    [Fact]
    public async Task Delete_BecomesTombstone()
    {
        var repo = New();
        var item = SampleItem();
        await repo.AddItemAsync(item);
        await repo.DeleteItemAsync(item.Id);

        Assert.Empty(await repo.GetItemsAsync());
        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.True(env.Deleted);
        Assert.Equal(string.Empty, env.PayloadJson);
        Assert.Equal(2, env.Version.Version);
    }

    [Fact]
    public async Task Archive_BumpsVersionAndMarksPending()
    {
        var repo = New();
        var item = SampleItem();
        await repo.AddItemAsync(item);
        await repo.ArchiveItemAsync(item.Id);

        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.Equal(2, env.Version.Version);
        Assert.False(env.Deleted);
        // is_active becomes 0; should be visible inside payload
        Assert.Contains("\"is_active\":false", env.PayloadJson);
    }

    [Fact]
    public async Task MarkPushed_ClearsPendingFlag()
    {
        var repo = New();
        var item = SampleItem();
        await repo.AddItemAsync(item);

        await repo.MarkPushedAsync(new[] { item.Id });

        Assert.Empty(await repo.GetPendingPushAsync());
        Assert.NotNull(await repo.GetByIdAsync(item.Id));
    }

    [Fact]
    public async Task ApplyRemote_InsertsNewEntity()
    {
        var repo = New();
        var item = SampleItem();
        var env = AssetSyncMapper.ToEnvelope(
            item,
            new EntityVersion(5, _time.GetUtcNow(), "device-B"),
            isDeleted: false);

        await repo.ApplyRemoteAsync(new[] { env });

        var stored = await repo.GetByIdAsync(item.Id);
        Assert.NotNull(stored);
        Assert.Equal("玉山活存", stored!.Name);
        Assert.Empty(await repo.GetPendingPushAsync());
    }

    [Fact]
    public async Task ApplyRemote_DoesNotWriteBackwards()
    {
        var repo = New();
        var item = SampleItem();
        await repo.AddItemAsync(item);
        await repo.UpdateItemAsync(item with { Name = "本地" });

        var stale = AssetSyncMapper.ToEnvelope(
            item with { Name = "雲端舊版" },
            new EntityVersion(1, _time.GetUtcNow().AddDays(1), "device-B"),
            isDeleted: false);

        await repo.ApplyRemoteAsync(new[] { stale });

        var stored = await repo.GetByIdAsync(item.Id);
        Assert.Equal("本地", stored!.Name);
    }

    [Fact]
    public async Task ApplyRemote_TombstoneDeletesLocally()
    {
        var repo = New();
        var item = SampleItem();
        await repo.AddItemAsync(item);

        var tombstone = new SyncEnvelope(
            item.Id, "Asset", string.Empty,
            new EntityVersion(99, _time.GetUtcNow(), "device-B"),
            Deleted: true);

        await repo.ApplyRemoteAsync(new[] { tombstone });

        Assert.Null(await repo.GetByIdAsync(item.Id));
    }

    [Fact]
    public async Task ApplyRemote_TombstoneForUnknownId_StoresHidden()
    {
        var repo = New();
        var unknown = Guid.NewGuid();
        var tombstone = new SyncEnvelope(
            unknown, "Asset", string.Empty,
            new EntityVersion(1, _time.GetUtcNow(), "device-B"),
            Deleted: true);

        await repo.ApplyRemoteAsync(new[] { tombstone });

        Assert.Null(await repo.GetByIdAsync(unknown));
        Assert.Empty(await repo.GetPendingPushAsync());
    }

    [Fact]
    public async Task GetItems_FiltersOutTombstones()
    {
        var repo = New();
        var keep = SampleItem();
        var drop = SampleItem() with { Name = "drop" };
        await repo.AddItemAsync(keep);
        await repo.AddItemAsync(drop);
        await repo.DeleteItemAsync(drop.Id);

        var all = await repo.GetItemsAsync();
        Assert.Single(all);
        Assert.Equal(keep.Id, all[0].Id);
    }

    [Fact]
    public async Task FindOrCreateAccount_NewAccount_StampsSyncMetadata()
    {
        var repo = New();
        var id = await repo.FindOrCreateAccountAsync("新帳戶", "TWD");

        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.Equal(id, env.EntityId);
        Assert.Equal(1, env.Version.Version);
    }

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTime(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
