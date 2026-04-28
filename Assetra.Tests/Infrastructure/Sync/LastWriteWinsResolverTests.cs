using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Infrastructure.Sync;

public sealed class LastWriteWinsResolverTests
{
    private static SyncEnvelope Make(long version, DateTimeOffset modifiedAt, string device = "d")
        => new(
            EntityId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            EntityType: "Trade",
            PayloadJson: "{}",
            Version: new EntityVersion(version, modifiedAt, device));

    [Fact]
    public void Resolve_LocalNewerTimestamp_KeepsLocal()
    {
        var t = DateTimeOffset.UtcNow;
        var c = new SyncConflict(Make(2, t.AddSeconds(1)), Make(2, t));
        Assert.Equal(SyncResolution.KeepLocal, new LastWriteWinsResolver().Resolve(c));
    }

    [Fact]
    public void Resolve_RemoteNewerTimestamp_KeepsRemote()
    {
        var t = DateTimeOffset.UtcNow;
        var c = new SyncConflict(Make(5, t), Make(2, t.AddSeconds(1)));
        Assert.Equal(SyncResolution.KeepRemote, new LastWriteWinsResolver().Resolve(c));
    }

    [Fact]
    public void Resolve_TieOnTimestamp_HigherVersionWins()
    {
        var t = DateTimeOffset.UtcNow;
        var c = new SyncConflict(Make(7, t), Make(3, t));
        Assert.Equal(SyncResolution.KeepLocal, new LastWriteWinsResolver().Resolve(c));
    }

    [Fact]
    public void Resolve_TotalTie_FavoursRemoteForDeterminism()
    {
        var t = DateTimeOffset.UtcNow;
        var c = new SyncConflict(Make(3, t), Make(3, t));
        Assert.Equal(SyncResolution.KeepRemote, new LastWriteWinsResolver().Resolve(c));
    }

    [Fact]
    public void Resolve_NullConflict_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new LastWriteWinsResolver().Resolve(null!));
    }
}
