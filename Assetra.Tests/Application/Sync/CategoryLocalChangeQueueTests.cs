using Assetra.Application.Sync;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;
using Xunit;

namespace Assetra.Tests.Application.Sync;

public sealed class CategoryLocalChangeQueueTests
{
    [Fact]
    public async Task GetPending_DelegatesToStore()
    {
        var fake = new FakeCategorySyncStore
        {
            Pending = new List<SyncEnvelope>
            {
                new(Guid.NewGuid(), "Category", "{}", new EntityVersion(1, DateTimeOffset.UtcNow, "d")),
            },
        };
        var q = new CategoryLocalChangeQueue(fake);

        var pending = await q.GetPendingAsync();
        Assert.Single(pending);
    }

    [Fact]
    public async Task MarkPushed_DelegatesToStore()
    {
        var fake = new FakeCategorySyncStore();
        var q = new CategoryLocalChangeQueue(fake);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };

        await q.MarkPushedAsync(ids);

        Assert.Equal(ids, fake.Pushed);
    }

    [Fact]
    public async Task ApplyRemote_DelegatesToStore()
    {
        var fake = new FakeCategorySyncStore();
        var q = new CategoryLocalChangeQueue(fake);
        var envs = new[]
        {
            new SyncEnvelope(Guid.NewGuid(), "Category", "{}", new EntityVersion(1, DateTimeOffset.UtcNow, "d")),
        };

        await q.ApplyRemoteAsync(envs);

        Assert.Equal(envs, fake.Applied);
    }

    [Fact]
    public async Task ManualConflict_StoredInMemoryAndDrainable()
    {
        var fake = new FakeCategorySyncStore();
        var q = new CategoryLocalChangeQueue(fake);
        var local = new SyncEnvelope(Guid.NewGuid(), "Category", "{}", new EntityVersion(1, DateTimeOffset.UtcNow, "d"));
        var remote = local with { Version = local.Version with { Version = 2 } };
        var conflict = new SyncConflict(local, remote);

        await q.RecordManualConflictAsync(new[] { conflict });

        var drained = q.DrainManualConflicts();
        Assert.Single(drained);
        Assert.Empty(q.DrainManualConflicts());
    }

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CategoryLocalChangeQueue(null!));
    }

    private sealed class FakeCategorySyncStore : ICategorySyncStore
    {
        public List<SyncEnvelope> Pending { get; set; } = new();
        public List<Guid> Pushed { get; } = new();
        public List<SyncEnvelope> Applied { get; } = new();

        public Task<IReadOnlyList<SyncEnvelope>> GetPendingPushAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SyncEnvelope>>(Pending);

        public Task MarkPushedAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
        {
            Pushed.AddRange(ids);
            return Task.CompletedTask;
        }

        public Task ApplyRemoteAsync(IReadOnlyList<SyncEnvelope> envelopes, CancellationToken ct = default)
        {
            Applied.AddRange(envelopes);
            return Task.CompletedTask;
        }
    }
}
