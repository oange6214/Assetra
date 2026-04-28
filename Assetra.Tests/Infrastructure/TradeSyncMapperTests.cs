using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class TradeSyncMapperTests
{
    private static Trade Sample() => new(
        Id: Guid.NewGuid(),
        Symbol: "2330",
        Exchange: "TWSE",
        Name: "台積電",
        Type: TradeType.Buy,
        TradeDate: DateTime.Parse("2026-04-01T00:00:00Z").ToUniversalTime(),
        Price: 800.5m,
        Quantity: 1000,
        RealizedPnl: 12.34m,
        RealizedPnlPct: 0.05m,
        CashAmount: 800500m,
        CashAccountId: Guid.NewGuid(),
        Note: "備註",
        Commission: 50.25m,
        LoanLabel: null,
        ToCashAccountId: null);

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var t = Sample();
        var env = TradeSyncMapper.ToEnvelope(
            t,
            new EntityVersion(3, DateTimeOffset.UtcNow, "dev"),
            isDeleted: false);
        var back = TradeSyncMapper.FromPayload(env);

        Assert.Equal(t.Id, back.Id);
        Assert.Equal(t.Symbol, back.Symbol);
        Assert.Equal(t.Name, back.Name);
        Assert.Equal(t.Type, back.Type);
        Assert.Equal(t.Price, back.Price);
        Assert.Equal(t.Quantity, back.Quantity);
        Assert.Equal(t.CashAmount, back.CashAmount);
        Assert.Equal(t.CashAccountId, back.CashAccountId);
        Assert.Equal(t.Note, back.Note);
        Assert.Equal(t.Commission, back.Commission);
        Assert.Equal(t.RealizedPnl, back.RealizedPnl);
    }

    [Fact]
    public void Tombstone_HasEmptyPayload()
    {
        var env = TradeSyncMapper.ToEnvelope(
            Sample(),
            new EntityVersion(2, DateTimeOffset.UtcNow, "dev"),
            isDeleted: true);

        Assert.True(env.Deleted);
        Assert.Equal(string.Empty, env.PayloadJson);
        Assert.Equal("Trade", env.EntityType);
    }

    [Fact]
    public void FromPayload_ThrowsOnTombstone()
    {
        var env = new SyncEnvelope(
            Guid.NewGuid(), "Trade", string.Empty,
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"),
            Deleted: true);
        Assert.Throws<InvalidOperationException>(() => TradeSyncMapper.FromPayload(env));
    }

    [Fact]
    public void FromPayload_ThrowsOnWrongEntityType()
    {
        var env = new SyncEnvelope(
            Guid.NewGuid(), "Category", "{}",
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"),
            Deleted: false);
        Assert.Throws<ArgumentException>(() => TradeSyncMapper.FromPayload(env));
    }

    [Fact]
    public void Payload_UsesSnakeCaseAndUnescapedCjk()
    {
        var t = Sample();
        var env = TradeSyncMapper.ToEnvelope(
            t,
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"),
            isDeleted: false);

        Assert.Contains("\"trade_type\"", env.PayloadJson);
        Assert.Contains("\"cash_account_id\"", env.PayloadJson);
        Assert.Contains("台積電", env.PayloadJson); // not escaped
    }
}
