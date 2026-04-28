using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Infrastructure.Sync;

public sealed class CategorySyncMapperTests
{
    private static EntityVersion MakeVersion(long v = 1, string device = "device-A")
        => new(v, DateTimeOffset.Parse("2026-04-28T00:00:00Z"), device);

    [Fact]
    public void Roundtrip_PreservesAllFields()
    {
        var original = new ExpenseCategory(
            Id: Guid.NewGuid(),
            Name: "餐飲",
            Kind: CategoryKind.Expense,
            ParentId: Guid.NewGuid(),
            Icon: "utensils",
            ColorHex: "#FF8800",
            SortOrder: 7,
            IsArchived: true);

        var envelope = CategorySyncMapper.ToEnvelope(original, MakeVersion(), isDeleted: false);
        var decoded = CategorySyncMapper.FromPayload(envelope);

        Assert.Equal(original, decoded);
        Assert.Equal("Category", envelope.EntityType);
        Assert.Equal(original.Id, envelope.EntityId);
        Assert.False(envelope.Deleted);
    }

    [Fact]
    public void Tombstone_HasEmptyPayload()
    {
        var c = new ExpenseCategory(Guid.NewGuid(), "x", CategoryKind.Expense);
        var env = CategorySyncMapper.ToEnvelope(c, MakeVersion(2), isDeleted: true);

        Assert.True(env.Deleted);
        Assert.Equal(string.Empty, env.PayloadJson);
        Assert.Equal(c.Id, env.EntityId);
    }

    [Fact]
    public void FromPayload_RejectsTombstone()
    {
        var c = new ExpenseCategory(Guid.NewGuid(), "x", CategoryKind.Expense);
        var env = CategorySyncMapper.ToEnvelope(c, MakeVersion(), isDeleted: true);
        Assert.Throws<InvalidOperationException>(() => CategorySyncMapper.FromPayload(env));
    }

    [Fact]
    public void FromPayload_RejectsWrongEntityType()
    {
        var env = new SyncEnvelope(Guid.NewGuid(), "Trade", "{}", MakeVersion());
        Assert.Throws<ArgumentException>(() => CategorySyncMapper.FromPayload(env));
    }

    [Fact]
    public void Payload_UsesSnakeCase()
    {
        var c = new ExpenseCategory(
            Id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name: "n",
            Kind: CategoryKind.Income,
            ParentId: null,
            Icon: null,
            ColorHex: "#000000",
            SortOrder: 3,
            IsArchived: false);
        var env = CategorySyncMapper.ToEnvelope(c, MakeVersion(), isDeleted: false);
        Assert.Contains("\"color_hex\"", env.PayloadJson);
        Assert.Contains("\"parent_id\"", env.PayloadJson);
        Assert.Contains("\"sort_order\"", env.PayloadJson);
        Assert.Contains("\"is_archived\"", env.PayloadJson);
    }
}
