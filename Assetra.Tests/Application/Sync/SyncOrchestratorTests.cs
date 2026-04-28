using Assetra.Application.Sync;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Application.Sync;

public sealed class SyncOrchestratorTests
{
    private const string DeviceId = "device-A";

    private static SyncEnvelope MakeEnvelope(
        Guid id,
        long version,
        DateTimeOffset at,
        string device = DeviceId,
        string payload = "{}")
        => new(id, "Trade", payload, new EntityVersion(version, at, device));

    private static (
        SyncOrchestrator Orchestrator,
        InMemoryCloudSyncProvider Provider,
        InMemoryLocalChangeQueue Queue,
        InMemorySyncMetadataStore Store,
        FixedTimeProvider Time)
        Build(IConflictResolver? resolver = null, IEnumerable<SyncEnvelope>? pending = null)
    {
        var provider = new InMemoryCloudSyncProvider();
        var queue = pending is null
            ? new InMemoryLocalChangeQueue()
            : new InMemoryLocalChangeQueue(pending);
        var store = new InMemorySyncMetadataStore(DeviceId);
        var time = new FixedTimeProvider(DateTimeOffset.Parse("2026-04-28T00:00:00Z"));
        var orchestrator = new SyncOrchestrator(
            provider,
            queue,
            store,
            resolver ?? new LastWriteWinsResolver(),
            time);
        return (orchestrator, provider, queue, store, time);
    }

    [Fact]
    public async Task SyncAsync_PullOnly_AppliesRemoteAndAdvancesCursor()
    {
        var (orch, provider, queue, store, time) = Build();
        var t = time.GetUtcNow();
        var idRemote = Guid.NewGuid();
        await provider.PushAsync(SyncMetadata.Empty("device-B"),
            new[] { MakeEnvelope(idRemote, 1, t, "device-B") });

        var result = await orch.SyncAsync();

        Assert.Equal(1, result.PulledCount);
        Assert.Equal(0, result.PushedCount);
        Assert.Equal(0, result.AutoResolvedConflicts);
        Assert.Equal(0, result.ManualConflicts);
        Assert.Single(queue.AppliedRemotes);
        Assert.Equal(idRemote, queue.AppliedRemotes[0].EntityId);

        var meta = await store.GetAsync();
        Assert.Equal("1", meta.Cursor);
        Assert.Equal(time.GetUtcNow(), meta.LastSyncAt);
    }

    [Fact]
    public async Task SyncAsync_PushOnly_PushesPendingAndMarksAccepted()
    {
        var t = DateTimeOffset.Parse("2026-04-27T00:00:00Z");
        var id = Guid.NewGuid();
        var pending = new[] { MakeEnvelope(id, 1, t) };
        var (orch, _, queue, store, time) = Build(pending: pending);

        var result = await orch.SyncAsync();

        Assert.Equal(0, result.PulledCount);
        Assert.Equal(1, result.PushedCount);
        var stillPending = await queue.GetPendingAsync();
        Assert.Empty(stillPending);

        var meta = await store.GetAsync();
        Assert.Equal("1", meta.Cursor);
        Assert.Equal(time.GetUtcNow(), meta.LastSyncAt);
    }

    [Fact]
    public async Task SyncAsync_PullAndPushMixed_BothApplied()
    {
        var t = DateTimeOffset.Parse("2026-04-27T00:00:00Z");
        var idLocal = Guid.NewGuid();
        var pending = new[] { MakeEnvelope(idLocal, 1, t) };
        var (orch, provider, queue, _, _) = Build(pending: pending);

        var idRemote = Guid.NewGuid();
        await provider.PushAsync(SyncMetadata.Empty("device-B"),
            new[] { MakeEnvelope(idRemote, 1, t, "device-B") });

        var result = await orch.SyncAsync();

        Assert.Equal(1, result.PulledCount);
        Assert.Equal(1, result.PushedCount);
        Assert.Equal(idRemote, queue.AppliedRemotes[0].EntityId);
        Assert.Empty(await queue.GetPendingAsync());
    }

    [Fact]
    public async Task SyncAsync_KeepLocalConflict_BumpsVersionAndRetriesPush()
    {
        var id = Guid.NewGuid();
        var tOld = DateTimeOffset.Parse("2026-04-26T00:00:00Z");
        var tNew = DateTimeOffset.Parse("2026-04-27T00:00:00Z");

        // Seed remote with version 5
        var provider = new InMemoryCloudSyncProvider();
        await provider.PushAsync(SyncMetadata.Empty("device-B"),
            new[] { MakeEnvelope(id, 5, tOld, "device-B", "{\"remote\":true}") });

        // Local has stale version 3 but newer timestamp → LWW picks local
        var local = MakeEnvelope(id, 3, tNew, DeviceId, "{\"local\":true}");
        var queue = new InMemoryLocalChangeQueue(new[] { local });
        var store = new InMemorySyncMetadataStore(DeviceId);
        var time = new FixedTimeProvider(DateTimeOffset.Parse("2026-04-28T00:00:00Z"));
        var orch = new SyncOrchestrator(provider, queue, store, new LastWriteWinsResolver(), time);

        var result = await orch.SyncAsync();

        Assert.Equal(1, result.AutoResolvedConflicts);
        Assert.Equal(0, result.ManualConflicts);
        // After bumped retry the local payload is now the server value
        var pull = await provider.PullAsync(SyncMetadata.Empty("device-C"));
        var server = Assert.Single(pull.Envelopes);
        Assert.Equal(id, server.EntityId);
        Assert.Equal(6, server.Version.Version);
        Assert.Equal("{\"local\":true}", server.PayloadJson);
    }

    [Fact]
    public async Task SyncAsync_KeepRemoteConflict_AppliesRemoteToQueue()
    {
        var id = Guid.NewGuid();
        var tOld = DateTimeOffset.Parse("2026-04-26T00:00:00Z");
        var tNew = DateTimeOffset.Parse("2026-04-27T00:00:00Z");

        var provider = new InMemoryCloudSyncProvider();
        // Remote has newer timestamp → LWW picks remote
        await provider.PushAsync(SyncMetadata.Empty("device-B"),
            new[] { MakeEnvelope(id, 5, tNew, "device-B", "{\"remote\":true}") });

        var local = MakeEnvelope(id, 3, tOld, DeviceId, "{\"local\":true}");
        var queue = new InMemoryLocalChangeQueue(new[] { local });
        var store = new InMemorySyncMetadataStore(DeviceId);
        var time = new FixedTimeProvider(DateTimeOffset.Parse("2026-04-28T00:00:00Z"));
        var orch = new SyncOrchestrator(provider, queue, store, new LastWriteWinsResolver(), time);

        var result = await orch.SyncAsync();

        Assert.Equal(1, result.AutoResolvedConflicts);
        Assert.Equal(0, result.ManualConflicts);
        // Pull pre-applied the remote envelope; the conflict adoption appended again.
        Assert.Contains(queue.AppliedRemotes,
            e => e.EntityId == id && e.PayloadJson == "{\"remote\":true}");
    }

    [Fact]
    public async Task SyncAsync_ManualConflict_RecordedForUI()
    {
        var id = Guid.NewGuid();
        var t = DateTimeOffset.Parse("2026-04-26T00:00:00Z");

        var provider = new InMemoryCloudSyncProvider();
        await provider.PushAsync(SyncMetadata.Empty("device-B"),
            new[] { MakeEnvelope(id, 5, t, "device-B") });

        var local = MakeEnvelope(id, 3, t.AddSeconds(1));
        var queue = new InMemoryLocalChangeQueue(new[] { local });
        var store = new InMemorySyncMetadataStore(DeviceId);
        var time = new FixedTimeProvider(DateTimeOffset.Parse("2026-04-28T00:00:00Z"));
        var orch = new SyncOrchestrator(provider, queue, store, new ManualResolver(), time);

        var result = await orch.SyncAsync();

        Assert.Equal(0, result.AutoResolvedConflicts);
        Assert.Equal(1, result.ManualConflicts);
        Assert.Single(queue.ManualConflicts);
        Assert.Equal(id, queue.ManualConflicts[0].EntityId);
    }

    [Fact]
    public async Task SyncAsync_KeepLocalRetry_StillConflicts_GoesToManual()
    {
        var id = Guid.NewGuid();
        var tBase = DateTimeOffset.Parse("2026-04-26T00:00:00Z");

        // Use a flaky provider that rejects every push for `id` to simulate persistent server-side conflict
        var provider = new FlakyAlwaysConflictProvider(id, serverVersion: 5, serverDevice: "device-B");
        var local = MakeEnvelope(id, 3, tBase.AddSeconds(1));
        var queue = new InMemoryLocalChangeQueue(new[] { local });
        var store = new InMemorySyncMetadataStore(DeviceId);
        var time = new FixedTimeProvider(DateTimeOffset.Parse("2026-04-28T00:00:00Z"));
        var orch = new SyncOrchestrator(provider, queue, store, new KeepLocalResolver(), time);

        var result = await orch.SyncAsync();

        Assert.Equal(1, result.AutoResolvedConflicts); // KeepLocal counted once
        Assert.Equal(1, result.ManualConflicts);
        Assert.Single(queue.ManualConflicts);
    }

    private sealed class KeepLocalResolver : IConflictResolver
    {
        public SyncResolution Resolve(SyncConflict conflict) => SyncResolution.KeepLocal;
    }

    [Fact]
    public async Task SyncAsync_NoChanges_StillSavesMetadata()
    {
        var (orch, _, _, store, time) = Build();

        var result = await orch.SyncAsync();

        Assert.Equal(0, result.PulledCount);
        Assert.Equal(0, result.PushedCount);
        var meta = await store.GetAsync();
        Assert.Equal(time.GetUtcNow(), meta.LastSyncAt);
    }

    [Fact]
    public void Constructor_NullDependencies_Throws()
    {
        var provider = new InMemoryCloudSyncProvider();
        var queue = new InMemoryLocalChangeQueue();
        var store = new InMemorySyncMetadataStore(DeviceId);
        var resolver = new LastWriteWinsResolver();

        Assert.Throws<ArgumentNullException>(() =>
            new SyncOrchestrator(null!, queue, store, resolver));
        Assert.Throws<ArgumentNullException>(() =>
            new SyncOrchestrator(provider, null!, store, resolver));
        Assert.Throws<ArgumentNullException>(() =>
            new SyncOrchestrator(provider, queue, null!, resolver));
        Assert.Throws<ArgumentNullException>(() =>
            new SyncOrchestrator(provider, queue, store, null!));
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class ManualResolver : IConflictResolver
    {
        public SyncResolution Resolve(SyncConflict conflict) => SyncResolution.Manual;
    }

    /// <summary>
    /// Provider that always returns a conflict for the configured entity id, regardless of incoming version.
    /// Used to verify second-round conflicts after KeepLocal retry land in manual queue.
    /// </summary>
    private sealed class FlakyAlwaysConflictProvider : ICloudSyncProvider
    {
        private readonly Guid _id;
        private readonly long _serverVersion;
        private readonly string _serverDevice;

        public FlakyAlwaysConflictProvider(Guid id, long serverVersion, string serverDevice)
        {
            _id = id;
            _serverVersion = serverVersion;
            _serverDevice = serverDevice;
        }

        public Task<SyncPullResult> PullAsync(SyncMetadata metadata, CancellationToken ct = default)
            => Task.FromResult(new SyncPullResult(Array.Empty<SyncEnvelope>(), metadata.Cursor));

        public Task<SyncPushResult> PushAsync(
            SyncMetadata metadata,
            IReadOnlyList<SyncEnvelope> envelopes,
            CancellationToken ct = default)
        {
            var conflicts = new List<SyncConflict>();
            var accepted = new List<Guid>();
            foreach (var e in envelopes)
            {
                if (e.EntityId == _id)
                {
                    var remote = new SyncEnvelope(
                        _id,
                        e.EntityType,
                        "{\"server\":true}",
                        new EntityVersion(_serverVersion, DateTimeOffset.UtcNow, _serverDevice));
                    conflicts.Add(new SyncConflict(Local: e, Remote: remote));
                }
                else
                {
                    accepted.Add(e.EntityId);
                }
            }
            return Task.FromResult(new SyncPushResult(accepted, conflicts, metadata.Cursor));
        }
    }
}
