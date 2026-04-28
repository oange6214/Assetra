using Assetra.Application.Sync;
using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;
using Moq;
using Xunit;

namespace Assetra.Tests.Application.Sync;

public class CompositeLocalChangeQueueTests
{
    private static SyncEnvelope Env(Guid id, string type) =>
        new(id, type, "{}", new EntityVersion(1, DateTimeOffset.UtcNow, "dev"));

    [Fact]
    public async Task GetPending_AggregatesAcrossQueues()
    {
        var qA = new Mock<ILocalChangeQueue>();
        qA.Setup(q => q.GetPendingAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new[] { Env(Guid.NewGuid(), "A") });
        var qB = new Mock<ILocalChangeQueue>();
        qB.Setup(q => q.GetPendingAsync(It.IsAny<CancellationToken>()))
          .ReturnsAsync(new[] { Env(Guid.NewGuid(), "B") });

        var composite = new CompositeLocalChangeQueue(new Dictionary<string, ILocalChangeQueue>
        {
            ["A"] = qA.Object,
            ["B"] = qB.Object,
        });

        var pending = await composite.GetPendingAsync();
        Assert.Equal(2, pending.Count);
    }

    [Fact]
    public async Task ApplyRemote_RoutesByEntityType()
    {
        var qA = new Mock<ILocalChangeQueue>();
        var qB = new Mock<ILocalChangeQueue>();
        var composite = new CompositeLocalChangeQueue(new Dictionary<string, ILocalChangeQueue>
        {
            ["A"] = qA.Object,
            ["B"] = qB.Object,
        });

        var envA = Env(Guid.NewGuid(), "A");
        var envB1 = Env(Guid.NewGuid(), "B");
        var envB2 = Env(Guid.NewGuid(), "B");

        await composite.ApplyRemoteAsync(new[] { envA, envB1, envB2 });

        qA.Verify(q => q.ApplyRemoteAsync(
            It.Is<IReadOnlyList<SyncEnvelope>>(l => l.Count == 1 && l[0] == envA),
            It.IsAny<CancellationToken>()), Times.Once);
        qB.Verify(q => q.ApplyRemoteAsync(
            It.Is<IReadOnlyList<SyncEnvelope>>(l => l.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ApplyRemote_IgnoresUnknownType()
    {
        var qA = new Mock<ILocalChangeQueue>();
        var composite = new CompositeLocalChangeQueue(new Dictionary<string, ILocalChangeQueue>
        {
            ["A"] = qA.Object,
        });

        await composite.ApplyRemoteAsync(new[] { Env(Guid.NewGuid(), "Unknown") });

        qA.Verify(q => q.ApplyRemoteAsync(It.IsAny<IReadOnlyList<SyncEnvelope>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecordManualConflict_RoutesByEntityType()
    {
        var qA = new Mock<ICategorySyncStore>();
        var realA = new CategoryLocalChangeQueue(qA.Object);
        var qB = new Mock<ITradeSyncStore>();
        var realB = new TradeLocalChangeQueue(qB.Object);
        var composite = new CompositeLocalChangeQueue(new Dictionary<string, ILocalChangeQueue>
        {
            ["Category"] = realA,
            ["Trade"] = realB,
        });

        var catEnv = Env(Guid.NewGuid(), "Category");
        var trdEnv = Env(Guid.NewGuid(), "Trade");
        var conflicts = new[]
        {
            new SyncConflict(catEnv, catEnv with { Version = new EntityVersion(2, DateTimeOffset.UtcNow, "x") }),
            new SyncConflict(trdEnv, trdEnv with { Version = new EntityVersion(2, DateTimeOffset.UtcNow, "x") }),
        };

        await composite.RecordManualConflictAsync(conflicts);

        Assert.Single(realA.DrainManualConflicts());
        Assert.Single(realB.DrainManualConflicts());
    }

    [Fact]
    public async Task DrainManualConflicts_AggregatesAcrossQueues()
    {
        var qA = new CategoryLocalChangeQueue(new Mock<ICategorySyncStore>().Object);
        var qB = new TradeLocalChangeQueue(new Mock<ITradeSyncStore>().Object);
        var composite = new CompositeLocalChangeQueue(new Dictionary<string, ILocalChangeQueue>
        {
            ["Category"] = qA,
            ["Trade"] = qB,
        });

        var catEnv = Env(Guid.NewGuid(), "Category");
        var trdEnv = Env(Guid.NewGuid(), "Trade");
        await qA.RecordManualConflictAsync(new[] { new SyncConflict(catEnv, catEnv) });
        await qB.RecordManualConflictAsync(new[] { new SyncConflict(trdEnv, trdEnv) });

        var drained = composite.DrainManualConflicts();
        Assert.Equal(2, drained.Count);
    }
}
