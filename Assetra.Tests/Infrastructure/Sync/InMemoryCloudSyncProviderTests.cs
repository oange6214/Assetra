using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Infrastructure.Sync;

public sealed class InMemoryCloudSyncProviderTests
{
    private static SyncEnvelope MakeEnvelope(Guid id, long version, DateTimeOffset at, string payload = "{}")
        => new(id, "Trade", payload, new EntityVersion(version, at, "device-A"));

    [Fact]
    public async Task PullAsync_EmptyStore_ReturnsNoEnvelopesAndKeepsCursor()
    {
        var provider = new InMemoryCloudSyncProvider();
        var meta = new SyncMetadata("device-A", Cursor: "5");

        var result = await provider.PullAsync(meta);

        Assert.Empty(result.Envelopes);
        Assert.Equal("5", result.NextCursor);
    }

    [Fact]
    public async Task PushAsync_NewEntities_AllAccepted_AdvancesCursor()
    {
        var provider = new InMemoryCloudSyncProvider();
        var meta = SyncMetadata.Empty("device-A");
        var t = DateTimeOffset.UtcNow;
        var batch = new[]
        {
            MakeEnvelope(Guid.NewGuid(), 1, t),
            MakeEnvelope(Guid.NewGuid(), 1, t.AddSeconds(1)),
        };

        var push = await provider.PushAsync(meta, batch);

        Assert.Equal(2, push.Accepted.Count);
        Assert.Empty(push.Conflicts);
        Assert.Equal("2", push.NextCursor);
    }

    [Fact]
    public async Task PushAsync_StaleVersion_ReturnsConflictWithRemoteState()
    {
        var provider = new InMemoryCloudSyncProvider();
        var meta = SyncMetadata.Empty("device-A");
        var id = Guid.NewGuid();
        var t = DateTimeOffset.UtcNow;
        await provider.PushAsync(meta, new[] { MakeEnvelope(id, 5, t) });

        var stale = MakeEnvelope(id, 3, t.AddMinutes(-1), "{\"stale\":true}");
        var push = await provider.PushAsync(meta, new[] { stale });

        Assert.Empty(push.Accepted);
        Assert.Single(push.Conflicts);
        Assert.Equal(id, push.Conflicts[0].EntityId);
        Assert.Equal(5, push.Conflicts[0].Remote.Version.Version);
        Assert.Equal(3, push.Conflicts[0].Local.Version.Version);
    }

    [Fact]
    public async Task PushThenPull_OtherDeviceSeesAcceptedEnvelopes()
    {
        var provider = new InMemoryCloudSyncProvider();
        var t = DateTimeOffset.UtcNow;
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        await provider.PushAsync(SyncMetadata.Empty("device-A"), new[]
        {
            MakeEnvelope(idA, 1, t),
            MakeEnvelope(idB, 1, t.AddTicks(1)),
        });

        var pull = await provider.PullAsync(SyncMetadata.Empty("device-B"));

        Assert.Equal(2, pull.Envelopes.Count);
        Assert.Equal("2", pull.NextCursor);
        Assert.Contains(pull.Envelopes, e => e.EntityId == idA);
        Assert.Contains(pull.Envelopes, e => e.EntityId == idB);
    }

    [Fact]
    public async Task PullAsync_WithCursor_ReturnsOnlyNewerEnvelopes()
    {
        var provider = new InMemoryCloudSyncProvider();
        var t = DateTimeOffset.UtcNow;
        var first = await provider.PushAsync(
            SyncMetadata.Empty("device-A"),
            new[] { MakeEnvelope(Guid.NewGuid(), 1, t) });

        var idLater = Guid.NewGuid();
        await provider.PushAsync(
            SyncMetadata.Empty("device-A"),
            new[] { MakeEnvelope(idLater, 1, t.AddSeconds(1)) });

        var meta = new SyncMetadata("device-B", Cursor: first.NextCursor);
        var pull = await provider.PullAsync(meta);

        Assert.Single(pull.Envelopes);
        Assert.Equal(idLater, pull.Envelopes[0].EntityId);
    }

    [Fact]
    public async Task PushAsync_AcceptedAfterBumpedVersion()
    {
        var provider = new InMemoryCloudSyncProvider();
        var meta = SyncMetadata.Empty("device-A");
        var id = Guid.NewGuid();
        var t = DateTimeOffset.UtcNow;
        await provider.PushAsync(meta, new[] { MakeEnvelope(id, 1, t) });

        var bumped = MakeEnvelope(id, 2, t.AddSeconds(1), "{\"bumped\":true}");
        var push = await provider.PushAsync(meta, new[] { bumped });

        Assert.Single(push.Accepted);
        Assert.Empty(push.Conflicts);
    }

    [Fact]
    public async Task PullAsync_NullMetadata_Throws()
    {
        var provider = new InMemoryCloudSyncProvider();
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.PullAsync(null!));
    }

    [Fact]
    public async Task PushAsync_NullEnvelopes_Throws()
    {
        var provider = new InMemoryCloudSyncProvider();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            provider.PushAsync(SyncMetadata.Empty("d"), null!));
    }
}
