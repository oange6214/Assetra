using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class AssetSyncMapperTests
{
    private static AssetItem Sample() => new(
        Id: Guid.NewGuid(),
        Name: "玉山銀行",
        Type: FinancialType.Asset,
        GroupId: Guid.NewGuid(),
        Currency: "TWD",
        CreatedDate: new DateOnly(2026, 1, 1),
        IsActive: true,
        UpdatedAt: DateTime.Parse("2026-04-01T00:00:00Z").ToUniversalTime(),
        LoanAnnualRate: 0.0123m,
        LoanTermMonths: 240,
        LoanStartDate: new DateOnly(2026, 1, 1),
        LoanHandlingFee: 1500m,
        LiabilitySubtype: LiabilitySubtype.Loan,
        BillingDay: 5,
        DueDay: 20,
        CreditLimit: 100000m,
        IssuerName: "Issuer",
        Subtype: "房貸");

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var i = Sample();
        var env = AssetSyncMapper.ToEnvelope(
            i,
            new EntityVersion(3, DateTimeOffset.UtcNow, "dev"),
            isDeleted: false);
        var back = AssetSyncMapper.FromPayload(env);

        Assert.Equal(i.Id, back.Id);
        Assert.Equal(i.Name, back.Name);
        Assert.Equal(i.Type, back.Type);
        Assert.Equal(i.GroupId, back.GroupId);
        Assert.Equal(i.Currency, back.Currency);
        Assert.Equal(i.CreatedDate, back.CreatedDate);
        Assert.Equal(i.IsActive, back.IsActive);
        Assert.Equal(i.LoanAnnualRate, back.LoanAnnualRate);
        Assert.Equal(i.LoanTermMonths, back.LoanTermMonths);
        Assert.Equal(i.LoanStartDate, back.LoanStartDate);
        Assert.Equal(i.LoanHandlingFee, back.LoanHandlingFee);
        Assert.Equal(i.LiabilitySubtype, back.LiabilitySubtype);
        Assert.Equal(i.BillingDay, back.BillingDay);
        Assert.Equal(i.DueDay, back.DueDay);
        Assert.Equal(i.CreditLimit, back.CreditLimit);
        Assert.Equal(i.IssuerName, back.IssuerName);
        Assert.Equal(i.Subtype, back.Subtype);
    }

    [Fact]
    public void Tombstone_HasEmptyPayload()
    {
        var env = AssetSyncMapper.ToEnvelope(
            Sample(),
            new EntityVersion(2, DateTimeOffset.UtcNow, "dev"),
            isDeleted: true);

        Assert.True(env.Deleted);
        Assert.Equal(string.Empty, env.PayloadJson);
        Assert.Equal("Asset", env.EntityType);
    }

    [Fact]
    public void FromPayload_ThrowsOnTombstone()
    {
        var env = new SyncEnvelope(
            Guid.NewGuid(), "Asset", string.Empty,
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"),
            Deleted: true);
        Assert.Throws<InvalidOperationException>(() => AssetSyncMapper.FromPayload(env));
    }

    [Fact]
    public void FromPayload_ThrowsOnWrongEntityType()
    {
        var env = new SyncEnvelope(
            Guid.NewGuid(), "Trade", "{}",
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"),
            Deleted: false);
        Assert.Throws<ArgumentException>(() => AssetSyncMapper.FromPayload(env));
    }

    [Fact]
    public void Payload_UsesSnakeCaseAndUnescapedCjk()
    {
        var i = Sample();
        var env = AssetSyncMapper.ToEnvelope(
            i,
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"),
            isDeleted: false);

        Assert.Contains("\"financial_type\"", env.PayloadJson);
        Assert.Contains("\"liability_subtype\"", env.PayloadJson);
        Assert.Contains("玉山銀行", env.PayloadJson); // not escaped
    }
}
