using Assetra.Core.Models;
using Assetra.Core.Models.Sync;
using Assetra.Infrastructure.Sync;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class AutoCategorizationRuleSyncMapperTests
{
    private static AutoCategorizationRule Sample() => new(
        Id: Guid.NewGuid(),
        KeywordPattern: "全聯",
        CategoryId: Guid.NewGuid(),
        Priority: 5,
        IsEnabled: true,
        MatchCaseSensitive: false,
        Name: "雜貨",
        MatchField: AutoCategorizationMatchField.Memo,
        MatchType: AutoCategorizationMatchType.StartsWith,
        AppliesTo: AutoCategorizationScope.Import);

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var r = Sample();
        var env = AutoCategorizationRuleSyncMapper.ToEnvelope(
            r, new EntityVersion(3, DateTimeOffset.UtcNow, "dev"), isDeleted: false);
        var back = AutoCategorizationRuleSyncMapper.FromPayload(env);

        Assert.Equal(r.Id, back.Id);
        Assert.Equal(r.KeywordPattern, back.KeywordPattern);
        Assert.Equal(r.CategoryId, back.CategoryId);
        Assert.Equal(r.Priority, back.Priority);
        Assert.Equal(r.IsEnabled, back.IsEnabled);
        Assert.Equal(r.MatchCaseSensitive, back.MatchCaseSensitive);
        Assert.Equal(r.Name, back.Name);
        Assert.Equal(r.MatchField, back.MatchField);
        Assert.Equal(r.MatchType, back.MatchType);
        Assert.Equal(r.AppliesTo, back.AppliesTo);
    }

    [Fact]
    public void RoundTrip_NullName()
    {
        var r = Sample() with { Name = null };
        var env = AutoCategorizationRuleSyncMapper.ToEnvelope(
            r, new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), isDeleted: false);
        var back = AutoCategorizationRuleSyncMapper.FromPayload(env);
        Assert.Null(back.Name);
    }

    [Fact]
    public void Tombstone_HasEmptyPayload()
    {
        var env = AutoCategorizationRuleSyncMapper.ToEnvelope(
            Sample(), new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), isDeleted: true);
        Assert.True(env.Deleted);
        Assert.Equal(string.Empty, env.PayloadJson);
        Assert.Equal("AutoCategorizationRule", env.EntityType);
    }

    [Fact]
    public void FromPayload_ThrowsOnTombstone()
    {
        var env = new SyncEnvelope(
            Guid.NewGuid(), "AutoCategorizationRule", string.Empty,
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), Deleted: true);
        Assert.Throws<InvalidOperationException>(() => AutoCategorizationRuleSyncMapper.FromPayload(env));
    }

    [Fact]
    public void FromPayload_ThrowsOnWrongEntityType()
    {
        var env = new SyncEnvelope(
            Guid.NewGuid(), "Category", "{}",
            new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), Deleted: false);
        Assert.Throws<ArgumentException>(() => AutoCategorizationRuleSyncMapper.FromPayload(env));
    }

    [Fact]
    public void Payload_UsesSnakeCaseAndUnescapedCjk()
    {
        var env = AutoCategorizationRuleSyncMapper.ToEnvelope(
            Sample(), new EntityVersion(1, DateTimeOffset.UtcNow, "dev"), isDeleted: false);
        Assert.Contains("\"keyword_pattern\"", env.PayloadJson);
        Assert.Contains("\"category_id\"", env.PayloadJson);
        Assert.Contains("\"match_case_sensitive\"", env.PayloadJson);
        Assert.Contains("\"match_field\"", env.PayloadJson);
        Assert.Contains("\"match_type\"", env.PayloadJson);
        Assert.Contains("\"applies_to\"", env.PayloadJson);
        Assert.Contains("全聯", env.PayloadJson);
        Assert.Contains("雜貨", env.PayloadJson);
    }
}
