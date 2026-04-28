using System.IO;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Persistence;
using Assetra.Infrastructure.Sync;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public sealed class AssetSqliteRepositoryEventSyncTests : IDisposable
{
    private readonly string _dbPath;
    private readonly FixedTime _time = new(DateTimeOffset.Parse("2026-04-28T00:00:00Z"));

    public AssetSqliteRepositoryEventSyncTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"assetra-event-sync-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }

    private AssetSqliteRepository New(string device = "device-A")
        => new(_dbPath, device, _time);

    private static AssetItem SampleAsset(Guid? id = null) => new(
        Id: id ?? Guid.NewGuid(),
        Name: "玉山活存",
        Type: FinancialType.Asset,
        GroupId: null,
        Currency: "TWD",
        CreatedDate: new DateOnly(2026, 1, 1));

    private static AssetEvent SampleEvent(Guid assetId, Guid? id = null) => new(
        Id: id ?? Guid.NewGuid(),
        AssetId: assetId,
        EventType: AssetEventType.Valuation,
        EventDate: DateTime.Parse("2026-04-28T00:00:00Z").ToUniversalTime(),
        Amount: 100000m,
        Quantity: null,
        Note: "季度估值",
        CashAccountId: null,
        CreatedAt: DateTime.Parse("2026-04-28T03:14:15Z").ToUniversalTime());

    [Fact]
    public async Task AddEvent_StampsVersion1AndPendingPush()
    {
        var repo = New();
        var asset = SampleAsset();
        await repo.AddItemAsync(asset);

        var evt = SampleEvent(asset.Id);
        await repo.AddEventAsync(evt);

        var pending = await repo.GetEventPendingPushAsync();
        var env = Assert.Single(pending, p => p.EntityId == evt.Id);
        Assert.Equal("AssetEvent", env.EntityType);
        Assert.Equal(1, env.Version.Version);
        Assert.False(env.Deleted);
    }

    [Fact]
    public async Task DeleteEvent_BecomesTombstone()
    {
        var repo = New();
        var asset = SampleAsset();
        await repo.AddItemAsync(asset);
        var evt = SampleEvent(asset.Id);
        await repo.AddEventAsync(evt);

        await repo.DeleteEventAsync(evt.Id);

        Assert.DoesNotContain(await repo.GetEventsAsync(asset.Id), e => e.Id == evt.Id);
        var pending = await repo.GetEventPendingPushAsync();
        var env = Assert.Single(pending, p => p.EntityId == evt.Id);
        Assert.True(env.Deleted);
        Assert.Equal(2, env.Version.Version);
    }

    [Fact]
    public async Task MarkEventPushed_ClearsPendingFlag()
    {
        var repo = New();
        var asset = SampleAsset();
        await repo.AddItemAsync(asset);
        var evt = SampleEvent(asset.Id);
        await repo.AddEventAsync(evt);

        await repo.MarkEventPushedAsync(new[] { evt.Id });

        Assert.DoesNotContain(await repo.GetEventPendingPushAsync(), e => e.EntityId == evt.Id);
    }

    [Fact]
    public async Task ApplyEventRemote_InsertsNewEvent()
    {
        var repo = New();
        var asset = SampleAsset();
        await repo.AddItemAsync(asset);

        var evt = SampleEvent(asset.Id);
        var env = AssetEventSyncMapper.ToEnvelope(
            evt, new EntityVersion(5, _time.GetUtcNow(), "device-B"), isDeleted: false);

        await repo.ApplyEventRemoteAsync(new[] { env });

        var events = await repo.GetEventsAsync(asset.Id);
        Assert.Contains(events, e => e.Id == evt.Id);
    }

    [Fact]
    public async Task ApplyEventRemote_DoesNotWriteBackwards()
    {
        var repo = New();
        var asset = SampleAsset();
        await repo.AddItemAsync(asset);
        var evt = SampleEvent(asset.Id);
        await repo.AddEventAsync(evt);

        var stale = AssetEventSyncMapper.ToEnvelope(
            evt with { Note = "雲端舊" },
            new EntityVersion(0, _time.GetUtcNow(), "device-B"),
            isDeleted: false);

        await repo.ApplyEventRemoteAsync(new[] { stale });

        var events = await repo.GetEventsAsync(asset.Id);
        Assert.Equal("季度估值", events.Single(e => e.Id == evt.Id).Note);
    }

    [Fact]
    public async Task ApplyEventRemote_TombstoneDeletesLocally()
    {
        var repo = New();
        var asset = SampleAsset();
        await repo.AddItemAsync(asset);
        var evt = SampleEvent(asset.Id);
        await repo.AddEventAsync(evt);

        var tombstone = new SyncEnvelope(
            evt.Id, "AssetEvent", string.Empty,
            new EntityVersion(99, _time.GetUtcNow(), "device-B"),
            Deleted: true);

        await repo.ApplyEventRemoteAsync(new[] { tombstone });

        Assert.DoesNotContain(await repo.GetEventsAsync(asset.Id), e => e.Id == evt.Id);
    }

    [Fact]
    public async Task ApplyEventRemote_TombstoneForUnknownId_StoresHidden()
    {
        var repo = New();
        var asset = SampleAsset();
        await repo.AddItemAsync(asset);
        var unknown = Guid.NewGuid();

        var tombstone = new SyncEnvelope(
            unknown, "AssetEvent", string.Empty,
            new EntityVersion(1, _time.GetUtcNow(), "device-B"),
            Deleted: true);

        await repo.ApplyEventRemoteAsync(new[] { tombstone });

        Assert.DoesNotContain(await repo.GetEventPendingPushAsync(), e => e.EntityId == unknown);
    }

    private sealed class FixedTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTime(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
