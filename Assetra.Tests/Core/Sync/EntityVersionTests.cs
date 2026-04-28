using Assetra.Core.Models.Sync;
using Xunit;

namespace Assetra.Tests.Core.Sync;

public sealed class EntityVersionTests
{
    [Fact]
    public void Initial_ProducesVersionOne()
    {
        var t = DateTimeOffset.UtcNow;
        var v = EntityVersion.Initial("device-A", t);

        Assert.Equal(1, v.Version);
        Assert.Equal(t, v.LastModifiedAt);
        Assert.Equal("device-A", v.LastModifiedByDevice);
    }

    [Fact]
    public void Bump_IncrementsVersionAndUpdatesMetadata()
    {
        var t = DateTimeOffset.UtcNow;
        var initial = EntityVersion.Initial("device-A", t);
        var bumped = initial.Bump("device-B", t.AddSeconds(5));

        Assert.Equal(2, bumped.Version);
        Assert.Equal(t.AddSeconds(5), bumped.LastModifiedAt);
        Assert.Equal("device-B", bumped.LastModifiedByDevice);
    }

    [Fact]
    public void Bump_NullDevice_TreatedAsEmptyString()
    {
        var v = EntityVersion.Initial("device-A", DateTimeOffset.UtcNow);
        var bumped = v.Bump(null!, DateTimeOffset.UtcNow);
        Assert.Equal(string.Empty, bumped.LastModifiedByDevice);
    }

    [Fact]
    public void DefaultRecord_RepresentsNeverSynced()
    {
        var v = new EntityVersion();
        Assert.Equal(0, v.Version);
        Assert.Equal(default, v.LastModifiedAt);
        Assert.Equal(string.Empty, v.LastModifiedByDevice);
    }
}
