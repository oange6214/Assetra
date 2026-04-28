using System.Globalization;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class RecurringTransactionSyncMapperTests
{
    private static RecurringTransaction Sample() => new(
        Id: Guid.NewGuid(),
        Name: "房租 🏠",
        TradeType: TradeType.Withdrawal,
        Amount: 12345.67m,
        CashAccountId: Guid.NewGuid(),
        CategoryId: Guid.NewGuid(),
        Frequency: RecurrenceFrequency.Monthly,
        Interval: 1,
        StartDate: new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc),
        EndDate: new DateTime(2027, 1, 5, 0, 0, 0, DateTimeKind.Utc),
        GenerationMode: AutoGenerationMode.AutoApply,
        LastGeneratedAt: new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc),
        NextDueAt: new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc),
        Note: "管理費已含",
        IsEnabled: true);

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var r = Sample();
        var env = RecurringTransactionSyncMapper.ToEnvelope(
            r, new EntityVersion(7, DateTimeOffset.UtcNow, "dev"), isDeleted: false);
        var back = RecurringTransactionSyncMapper.FromPayload(env);

        Assert.Equal(r.Id, back.Id);
        Assert.Equal(r.Name, back.Name);
        Assert.Equal(r.TradeType, back.TradeType);
        Assert.Equal(r.Amount, back.Amount);
        Assert.Equal(r.CashAccountId, back.CashAccountId);
        Assert.Equal(r.CategoryId, back.CategoryId);
        Assert.Equal(r.Frequency, back.Frequency);
        Assert.Equal(r.Interval, back.Interval);
        Assert.Equal(r.StartDate, back.StartDate);
        Assert.Equal(r.EndDate, back.EndDate);
        Assert.Equal(r.GenerationMode, back.GenerationMode);
        Assert.Equal(r.LastGeneratedAt, back.LastGeneratedAt);
        Assert.Equal(r.NextDueAt, back.NextDueAt);
        Assert.Equal(r.Note, back.Note);
        Assert.Equal(r.IsEnabled, back.IsEnabled);
    }

    [Fact]
    public void RoundTrip_AllNullables()
    {
        var r = Sample() with
        {
            CashAccountId = null,
            CategoryId = null,
            EndDate = null,
            LastGeneratedAt = null,
            NextDueAt = null,
            Note = null,
        };
        var env = RecurringTransactionSyncMapper.ToEnvelope(
            r, new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), isDeleted: false);
        var back = RecurringTransactionSyncMapper.FromPayload(env);
        Assert.Null(back.CashAccountId);
        Assert.Null(back.CategoryId);
        Assert.Null(back.EndDate);
        Assert.Null(back.LastGeneratedAt);
        Assert.Null(back.NextDueAt);
        Assert.Null(back.Note);
    }

    [Fact]
    public void Tombstone_HasEmptyPayload()
    {
        var env = RecurringTransactionSyncMapper.ToEnvelope(
            Sample(), new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), isDeleted: true);
        Assert.True(env.Deleted);
        Assert.Equal(string.Empty, env.PayloadJson);
        Assert.Equal("RecurringTransaction", env.EntityType);
    }

    [Fact]
    public void FromPayload_ThrowsOnTombstone()
    {
        var env = new SyncEnvelope(
            Guid.NewGuid(), "RecurringTransaction", string.Empty,
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), Deleted: true);
        Assert.Throws<InvalidOperationException>(() => RecurringTransactionSyncMapper.FromPayload(env));
    }

    [Fact]
    public void FromPayload_ThrowsOnWrongEntityType()
    {
        var env = new SyncEnvelope(
            Guid.NewGuid(), "Category", "{}",
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), Deleted: false);
        Assert.Throws<ArgumentException>(() => RecurringTransactionSyncMapper.FromPayload(env));
    }

    [Fact]
    public void Payload_AmountIsInvariantString()
    {
        var r = Sample() with { Amount = 1234.5m };
        var env = RecurringTransactionSyncMapper.ToEnvelope(
            r, new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), isDeleted: false);
        Assert.Contains("\"1234.5\"", env.PayloadJson);
        Assert.DoesNotContain("\"1,234.5\"", env.PayloadJson);
    }

    [Fact]
    public void Payload_UsesSnakeCaseAndUnescapedCjk()
    {
        var env = RecurringTransactionSyncMapper.ToEnvelope(
            Sample(), new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), isDeleted: false);
        Assert.Contains("\"trade_type\"", env.PayloadJson);
        Assert.Contains("\"cash_account_id\"", env.PayloadJson);
        Assert.Contains("\"interval_value\"", env.PayloadJson);
        Assert.Contains("\"start_date\"", env.PayloadJson);
        Assert.Contains("\"generation_mode\"", env.PayloadJson);
        Assert.Contains("\"last_generated_at\"", env.PayloadJson);
        Assert.Contains("\"next_due_at\"", env.PayloadJson);
        Assert.Contains("\"is_enabled\"", env.PayloadJson);
        Assert.Contains("房租", env.PayloadJson);
        Assert.Contains("管理費已含", env.PayloadJson);
    }
}
