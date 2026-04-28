using System.IO;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Persistence;
using Assetra.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public sealed class PortfolioSqliteRepositorySyncTests : IDisposable
{
    private readonly string _dbPath;
    private readonly FixedTime _time = new(DateTimeOffset.Parse("2026-04-28T00:00:00Z"));

    public PortfolioSqliteRepositorySyncTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"assetra-portfolio-sync-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private PortfolioSqliteRepository New(string device = "device-A")
        => new(_dbPath, device, _time);

    private static PortfolioEntry SampleEntry(Guid? id = null, string symbol = "2330") => new(
        Id: id ?? Guid.NewGuid(),
        Symbol: symbol,
        Exchange: "TWSE",
        AssetType: AssetType.Stock,
        DisplayName: "台積電",
        Currency: "TWD",
        IsActive: true,
        IsEtf: false);

    [Fact]
    public async Task Add_StampsVersion1AndPendingPush()
    {
        var repo = New();
        var e = SampleEntry();
        await repo.AddAsync(e);

        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.Equal(e.Id, env.EntityId);
        Assert.Equal("Portfolio", env.EntityType);
        Assert.Equal(1, env.Version.Version);
        Assert.Equal("device-A", env.Version.LastModifiedByDevice);
        Assert.False(env.Deleted);
    }

    [Fact]
    public async Task Update_BumpsVersion()
    {
        var repo = New();
        var e = SampleEntry();
        await repo.AddAsync(e);
        await repo.UpdateAsync(e with { AssetType = AssetType.Etf });

        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.Equal(2, env.Version.Version);
        Assert.Contains("Etf", env.PayloadJson);
    }

    [Fact]
    public async Task UpdateMetadata_BumpsVersion()
    {
        var repo = New();
        var e = SampleEntry();
        await repo.AddAsync(e);
        await repo.UpdateMetadataAsync(e.Id, "改名後", "USD");

        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.Equal(2, env.Version.Version);
        Assert.Contains("改名後", env.PayloadJson);
        Assert.Contains("USD", env.PayloadJson);
    }

    [Fact]
    public async Task Remove_BecomesTombstone()
    {
        var repo = New();
        var e = SampleEntry();
        await repo.AddAsync(e);
        await repo.RemoveAsync(e.Id);

        Assert.Empty(await repo.GetEntriesAsync());
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
        var e = SampleEntry();
        await repo.AddAsync(e);
        await repo.ArchiveAsync(e.Id);

        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.Equal(2, env.Version.Version);
        Assert.False(env.Deleted);
        Assert.Contains("\"is_active\":false", env.PayloadJson);
    }

    [Fact]
    public async Task Unarchive_BumpsVersion()
    {
        var repo = New();
        var e = SampleEntry();
        await repo.AddAsync(e);
        await repo.ArchiveAsync(e.Id);
        await repo.UnarchiveAsync(e.Id);

        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.Equal(3, env.Version.Version);
        Assert.Contains("\"is_active\":true", env.PayloadJson);
    }

    [Fact]
    public async Task MarkPushed_ClearsPendingFlag()
    {
        var repo = New();
        var e = SampleEntry();
        await repo.AddAsync(e);

        await repo.MarkPushedAsync(new[] { e.Id });

        Assert.Empty(await repo.GetPendingPushAsync());
        Assert.Single(await repo.GetEntriesAsync());
    }

    [Fact]
    public async Task ApplyRemote_InsertsNewEntity()
    {
        var repo = New();
        var e = SampleEntry();
        var env = PortfolioSyncMapper.ToEnvelope(
            e,
            new EntityVersion(5, _time.GetUtcNow(), "device-B"),
            isDeleted: false);

        await repo.ApplyRemoteAsync(new[] { env });

        var entries = await repo.GetEntriesAsync();
        var stored = Assert.Single(entries);
        Assert.Equal(e.Id, stored.Id);
        Assert.Equal("2330", stored.Symbol);
        Assert.Empty(await repo.GetPendingPushAsync());
    }

    [Fact]
    public async Task ApplyRemote_DoesNotWriteBackwards()
    {
        var repo = New();
        var e = SampleEntry();
        await repo.AddAsync(e);
        await repo.UpdateMetadataAsync(e.Id, "本地", "TWD");

        var stale = PortfolioSyncMapper.ToEnvelope(
            e with { DisplayName = "雲端舊版" },
            new EntityVersion(1, _time.GetUtcNow().AddDays(1), "device-B"),
            isDeleted: false);

        await repo.ApplyRemoteAsync(new[] { stale });

        var entries = await repo.GetEntriesAsync();
        var stored = Assert.Single(entries);
        Assert.Equal("本地", stored.DisplayName);
    }

    [Fact]
    public async Task ApplyRemote_TombstoneDeletesLocally()
    {
        var repo = New();
        var e = SampleEntry();
        await repo.AddAsync(e);

        var tombstone = new SyncEnvelope(
            e.Id, "Portfolio", string.Empty,
            new EntityVersion(99, _time.GetUtcNow(), "device-B"),
            Deleted: true);

        await repo.ApplyRemoteAsync(new[] { tombstone });

        Assert.Empty(await repo.GetEntriesAsync());
    }

    [Fact]
    public async Task ApplyRemote_TombstoneForUnknownId_StoresHidden()
    {
        var repo = New();
        var unknown = Guid.NewGuid();
        var tombstone = new SyncEnvelope(
            unknown, "Portfolio", string.Empty,
            new EntityVersion(1, _time.GetUtcNow(), "device-B"),
            Deleted: true);

        await repo.ApplyRemoteAsync(new[] { tombstone });

        Assert.Empty(await repo.GetEntriesAsync());
        Assert.Empty(await repo.GetPendingPushAsync());
    }

    [Fact]
    public async Task ApplyRemote_MultipleTombstones_DoNotCollide()
    {
        var repo = New();
        var t1 = new SyncEnvelope(
            Guid.NewGuid(), "Portfolio", string.Empty,
            new EntityVersion(1, _time.GetUtcNow(), "device-B"),
            Deleted: true);
        var t2 = new SyncEnvelope(
            Guid.NewGuid(), "Portfolio", string.Empty,
            new EntityVersion(1, _time.GetUtcNow(), "device-B"),
            Deleted: true);

        await repo.ApplyRemoteAsync(new[] { t1, t2 });

        Assert.Empty(await repo.GetEntriesAsync());
    }

    [Fact]
    public async Task GetEntries_FiltersOutTombstones()
    {
        var repo = New();
        var keep = SampleEntry(symbol: "2330");
        var drop = SampleEntry(symbol: "0050");
        await repo.AddAsync(keep);
        await repo.AddAsync(drop);
        await repo.RemoveAsync(drop.Id);

        var entries = await repo.GetEntriesAsync();
        var stored = Assert.Single(entries);
        Assert.Equal(keep.Id, stored.Id);
    }

    [Fact]
    public async Task FindOrCreate_NewEntry_StampsSyncMetadata()
    {
        var repo = New();
        var id = await repo.FindOrCreatePortfolioEntryAsync("2330", "TWSE", "台積電", AssetType.Stock);

        var pending = await repo.GetPendingPushAsync();
        var env = Assert.Single(pending);
        Assert.Equal(id, env.EntityId);
        Assert.Equal(1, env.Version.Version);
    }

    [Fact]
    public async Task FindOrCreate_Existing_ReturnsSameId()
    {
        var repo = New();
        var id1 = await repo.FindOrCreatePortfolioEntryAsync("2330", "TWSE", "台積電", AssetType.Stock);
        var id2 = await repo.FindOrCreatePortfolioEntryAsync("2330", "TWSE", "台積電", AssetType.Stock);

        Assert.Equal(id1, id2);
    }

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTime(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
