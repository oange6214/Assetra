using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class AssetGroupSyncMapperTests
{
    private static AssetGroup Sample() => new(
        Id: Guid.NewGuid(),
        Name: "🏦 銀行帳戶",
        Type: FinancialType.Asset,
        Icon: "bank",
        SortOrder: 3,
        IsSystem: false,
        CreatedDate: new DateOnly(2026, 1, 15));

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var g = Sample();
        var env = AssetGroupSyncMapper.ToEnvelope(
            g, new EntityVersion(2, DateTimeOffset.UtcNow, "dev"), isDeleted: false);
        var back = AssetGroupSyncMapper.FromPayload(env);

        Assert.Equal(g.Id, back.Id);
        Assert.Equal(g.Name, back.Name);
        Assert.Equal(g.Type, back.Type);
        Assert.Equal(g.Icon, back.Icon);
        Assert.Equal(g.SortOrder, back.SortOrder);
        Assert.Equal(g.IsSystem, back.IsSystem);
        Assert.Equal(g.CreatedDate, back.CreatedDate);
    }

    [Fact]
    public void Tombstone_HasEmptyPayload()
    {
        var env = AssetGroupSyncMapper.ToEnvelope(
            Sample(), new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), isDeleted: true);
        Assert.True(env.Deleted);
        Assert.Equal(string.Empty, env.PayloadJson);
        Assert.Equal("AssetGroup", env.EntityType);
    }

    [Fact]
    public void FromPayload_ThrowsOnTombstone()
    {
        var env = new SyncEnvelope(
            Guid.NewGuid(), "AssetGroup", string.Empty,
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), Deleted: true);
        Assert.Throws<InvalidOperationException>(() => AssetGroupSyncMapper.FromPayload(env));
    }

    [Fact]
    public void FromPayload_ThrowsOnWrongEntityType()
    {
        var env = new SyncEnvelope(
            Guid.NewGuid(), "Asset", "{}",
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), Deleted: false);
        Assert.Throws<ArgumentException>(() => AssetGroupSyncMapper.FromPayload(env));
    }

    [Fact]
    public void Payload_UsesSnakeCaseAndUnescapedCjk()
    {
        var env = AssetGroupSyncMapper.ToEnvelope(
            Sample(), new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), isDeleted: false);
        Assert.Contains("\"financial_type\"", env.PayloadJson);
        Assert.Contains("\"sort_order\"", env.PayloadJson);
        Assert.Contains("\"is_system\"", env.PayloadJson);
        Assert.Contains("\"created_date\"", env.PayloadJson);
        Assert.Contains("銀行帳戶", env.PayloadJson);
    }
}
