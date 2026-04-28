using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class AssetEventSyncMapperTests
{
    private static AssetEvent Sample() => new(
        Id: Guid.NewGuid(),
        AssetId: Guid.NewGuid(),
        EventType: AssetEventType.Valuation,
        EventDate: DateTime.Parse("2026-04-28T00:00:00Z").ToUniversalTime(),
        Amount: 1234567.89m,
        Quantity: 100.5m,
        Note: "備註：季度估值",
        CashAccountId: Guid.NewGuid(),
        CreatedAt: DateTime.Parse("2026-04-28T03:14:15Z").ToUniversalTime());

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var e = Sample();
        var env = AssetEventSyncMapper.ToEnvelope(
            e, new EntityVersion(2, DateTimeOffset.UtcNow, "dev"), isDeleted: false);
        var back = AssetEventSyncMapper.FromPayload(env);

        Assert.Equal(e.Id, back.Id);
        Assert.Equal(e.AssetId, back.AssetId);
        Assert.Equal(e.EventType, back.EventType);
        Assert.Equal(e.EventDate, back.EventDate);
        Assert.Equal(e.Amount, back.Amount);
        Assert.Equal(e.Quantity, back.Quantity);
        Assert.Equal(e.Note, back.Note);
        Assert.Equal(e.CashAccountId, back.CashAccountId);
        Assert.Equal(e.CreatedAt, back.CreatedAt);
    }

    [Fact]
    public void Tombstone_HasEmptyPayload()
    {
        var env = AssetEventSyncMapper.ToEnvelope(
            Sample(), new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), isDeleted: true);
        Assert.True(env.Deleted);
        Assert.Equal(string.Empty, env.PayloadJson);
        Assert.Equal("AssetEvent", env.EntityType);
    }

    [Fact]
    public void FromPayload_ThrowsOnTombstone()
    {
        var env = new SyncEnvelope(
            Guid.NewGuid(), "AssetEvent", string.Empty,
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), Deleted: true);
        Assert.Throws<InvalidOperationException>(() => AssetEventSyncMapper.FromPayload(env));
    }

    [Fact]
    public void FromPayload_ThrowsOnWrongEntityType()
    {
        var env = new SyncEnvelope(
            Guid.NewGuid(), "Asset", "{}",
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), Deleted: false);
        Assert.Throws<ArgumentException>(() => AssetEventSyncMapper.FromPayload(env));
    }

    [Fact]
    public void Payload_UsesSnakeCaseAndDecimalString()
    {
        var env = AssetEventSyncMapper.ToEnvelope(
            Sample(), new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), isDeleted: false);
        Assert.Contains("\"asset_id\"", env.PayloadJson);
        Assert.Contains("\"event_type\"", env.PayloadJson);
        Assert.Contains("\"event_date\"", env.PayloadJson);
        Assert.Contains("\"cash_account_id\"", env.PayloadJson);
        Assert.Contains("\"1234567.89\"", env.PayloadJson);
        Assert.Contains("季度估值", env.PayloadJson);
    }

    [Fact]
    public void NullableDecimals_RoundTripAsNull()
    {
        var e = Sample() with { Amount = null, Quantity = null, CashAccountId = null, Note = null };
        var env = AssetEventSyncMapper.ToEnvelope(
            e, new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), isDeleted: false);
        var back = AssetEventSyncMapper.FromPayload(env);
        Assert.Null(back.Amount);
        Assert.Null(back.Quantity);
        Assert.Null(back.CashAccountId);
        Assert.Null(back.Note);
    }
}
