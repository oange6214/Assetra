using Assetra.Core.DomainServices;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Core;

public sealed class AutoCategorizationEngineTests
{
    private static readonly Guid CatFood    = Guid.NewGuid();
    private static readonly Guid CatTransit = Guid.NewGuid();
    private static readonly Guid CatRent    = Guid.NewGuid();

    private static AutoCategorizationRule Rule(
        string keyword, Guid catId, int priority = 0,
        bool enabled = true, bool caseSensitive = false) =>
        new(Guid.NewGuid(), keyword, catId, priority, enabled, caseSensitive);

    [Fact]
    public void Match_ReturnsNull_WhenNoteIsNullOrWhitespace()
    {
        var rules = new[] { Rule("food", CatFood) };
        Assert.Null(AutoCategorizationEngine.Match(null, rules));
        Assert.Null(AutoCategorizationEngine.Match("   ", rules));
    }

    [Fact]
    public void Match_ReturnsNull_WhenNoRuleMatches()
    {
        var rules = new[] { Rule("food", CatFood) };
        Assert.Null(AutoCategorizationEngine.Match("salary", rules));
    }

    [Fact]
    public void Match_ReturnsCategoryId_WhenKeywordContained()
    {
        var rules = new[] { Rule("Starbucks", CatFood) };
        Assert.Equal(CatFood, AutoCategorizationEngine.Match("Visit Starbucks downtown", rules));
    }

    [Fact]
    public void Match_IsCaseInsensitiveByDefault()
    {
        var rules = new[] { Rule("STARBUCKS", CatFood) };
        Assert.Equal(CatFood, AutoCategorizationEngine.Match("starbucks coffee", rules));
    }

    [Fact]
    public void Match_RespectsCaseSensitiveFlag()
    {
        var rules = new[] { Rule("STARBUCKS", CatFood, caseSensitive: true) };
        Assert.Null(AutoCategorizationEngine.Match("starbucks coffee", rules));
        Assert.Equal(CatFood, AutoCategorizationEngine.Match("STARBUCKS coffee", rules));
    }

    [Fact]
    public void Match_PrefersLowerPriorityValue()
    {
        // Priority 越小越優先
        var rules = new[]
        {
            Rule("捷運", CatRent,    priority: 10),
            Rule("捷運", CatTransit, priority: 1),
        };
        Assert.Equal(CatTransit, AutoCategorizationEngine.Match("搭捷運", rules));
    }

    [Fact]
    public void Match_SkipsDisabledRules()
    {
        var rules = new[]
        {
            Rule("food", CatFood, priority: 1, enabled: false),
            Rule("food", CatRent, priority: 5, enabled: true),
        };
        Assert.Equal(CatRent, AutoCategorizationEngine.Match("food court", rules));
    }

    [Fact]
    public void Match_IgnoresEmptyKeywordPattern()
    {
        var rules = new[]
        {
            Rule("",     CatFood, priority: 1),
            Rule("rent", CatRent, priority: 5),
        };
        Assert.Equal(CatRent, AutoCategorizationEngine.Match("monthly rent", rules));
    }

    [Fact]
    public void Match_ThrowsArgumentNullException_WhenRulesIsNull()
    {
        Assert.Throws<ArgumentNullException>(
            () => AutoCategorizationEngine.Match("anything", null!));
    }
}
