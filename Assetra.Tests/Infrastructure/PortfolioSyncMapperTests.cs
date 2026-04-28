using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class PortfolioSyncMapperTests
{
    private static PortfolioEntry Sample() => new(
        Id: Guid.NewGuid(),
        Symbol: "2330",
        Exchange: "TWSE",
        AssetType: AssetType.Stock,
        DisplayName: "台積電",
        Currency: "TWD",
        IsActive: true,
        IsEtf: false);

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var e = Sample();
        var env = PortfolioSyncMapper.ToEnvelope(
            e,
            new EntityVersion(3, DateTimeOffset.UtcNow, "dev"),
            isDeleted: false);
        var back = PortfolioSyncMapper.FromPayload(env);

        Assert.Equal(e.Id, back.Id);
        Assert.Equal(e.Symbol, back.Symbol);
        Assert.Equal(e.Exchange, back.Exchange);
        Assert.Equal(e.AssetType, back.AssetType);
        Assert.Equal(e.DisplayName, back.DisplayName);
        Assert.Equal(e.Currency, back.Currency);
        Assert.Equal(e.IsActive, back.IsActive);
        Assert.Equal(e.IsEtf, back.IsEtf);
    }

    [Fact]
    public void Tombstone_HasEmptyPayload()
    {
        var env = PortfolioSyncMapper.ToEnvelope(
            Sample(),
            new EntityVersion(2, DateTimeOffset.UtcNow, "dev"),
            isDeleted: true);

        Assert.True(env.Deleted);
        Assert.Equal(string.Empty, env.PayloadJson);
        Assert.Equal("Portfolio", env.EntityType);
    }

    [Fact]
    public void FromPayload_ThrowsOnTombstone()
    {
        var env = new SyncEnvelope(
            Guid.NewGuid(), "Portfolio", string.Empty,
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"),
            Deleted: true);
        Assert.Throws<InvalidOperationException>(() => PortfolioSyncMapper.FromPayload(env));
    }

    [Fact]
    public void FromPayload_ThrowsOnWrongEntityType()
    {
        var env = new SyncEnvelope(
            Guid.NewGuid(), "Asset", "{}",
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"),
            Deleted: false);
        Assert.Throws<ArgumentException>(() => PortfolioSyncMapper.FromPayload(env));
    }

    [Fact]
    public void Payload_UsesSnakeCaseAndUnescapedCjk()
    {
        var e = Sample();
        var env = PortfolioSyncMapper.ToEnvelope(
            e,
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"),
            isDeleted: false);

        Assert.Contains("\"asset_type\"", env.PayloadJson);
        Assert.Contains("\"display_name\"", env.PayloadJson);
        Assert.Contains("\"is_etf\"", env.PayloadJson);
        Assert.Contains("台積電", env.PayloadJson);
    }
}
