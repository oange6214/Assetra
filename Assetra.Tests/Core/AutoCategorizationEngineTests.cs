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
        bool enabled = true, bool caseSensitive = false,
        AutoCategorizationMatchField field = AutoCategorizationMatchField.AnyText,
        AutoCategorizationMatchType type = AutoCategorizationMatchType.Contains,
        AutoCategorizationScope appliesTo = AutoCategorizationScope.Both) =>
        new(Guid.NewGuid(), keyword, catId, priority, enabled, caseSensitive,
            Name: null, MatchField: field, MatchType: type, AppliesTo: appliesTo);

    private static AutoCategorizationContext ImportCtx(string? counterparty = null, string? memo = null) =>
        new(Note: null, Counterparty: counterparty, Memo: memo, Source: AutoCategorizationScope.Import);

    [Fact]
    public void Match_Equals_RequiresExactValue()
    {
        var rule = Rule("水利處", CatRent,
            field: AutoCategorizationMatchField.Counterparty,
            type: AutoCategorizationMatchType.Equals);
        Assert.Equal(CatRent, AutoCategorizationEngine.Match(ImportCtx(counterparty: "水利處"), new[] { rule }));
        Assert.Null(AutoCategorizationEngine.Match(ImportCtx(counterparty: "水利處台北"), new[] { rule }));
    }

    [Fact]
    public void Match_StartsWith_OnMemo()
    {
        var rule = Rule("INV-", CatTransit,
            field: AutoCategorizationMatchField.Memo,
            type: AutoCategorizationMatchType.StartsWith);
        Assert.Equal(CatTransit, AutoCategorizationEngine.Match(ImportCtx(memo: "INV-2026"), new[] { rule }));
        Assert.Null(AutoCategorizationEngine.Match(ImportCtx(memo: "X-INV-2026"), new[] { rule }));
    }

    [Fact]
    public void Match_Regex_OnEither_AcrossCounterpartyAndMemo()
    {
        var rule = Rule(@"^7-?11", CatFood,
            field: AutoCategorizationMatchField.Either,
            type: AutoCategorizationMatchType.Regex);
        var rules = new[] { rule };
        Assert.Equal(CatFood, AutoCategorizationEngine.Match(ImportCtx(counterparty: "7-11 信義店"), rules));
        Assert.Equal(CatFood, AutoCategorizationEngine.Match(ImportCtx(memo: "711 北車"), rules));
        Assert.Null(AutoCategorizationEngine.Match(ImportCtx(counterparty: "全家"), rules));
    }

    [Fact]
    public void Match_BadRegex_IsSkipped()
    {
        var rule = Rule("([unclosed", CatFood, type: AutoCategorizationMatchType.Regex);
        Assert.Null(AutoCategorizationEngine.Match(ImportCtx(counterparty: "anything"), new[] { rule }));
    }

    [Fact]
    public void Match_AppliesTo_Manual_Skipped_InImportContext()
    {
        var rule = Rule("Coffee", CatFood, appliesTo: AutoCategorizationScope.Manual);
        Assert.Null(AutoCategorizationEngine.Match(ImportCtx(counterparty: "Coffee Bar"), new[] { rule }));
    }

    [Fact]
    public void Match_AppliesTo_Import_Skipped_InManualContext()
    {
        var rule = Rule("Coffee", CatFood, appliesTo: AutoCategorizationScope.Import);
        Assert.Null(AutoCategorizationEngine.Match("Coffee Bar", new[] { rule }));
    }

    [Fact]
    public void Match_AnyText_FallsBackToCounterpartyMemoConcat_InImportContext()
    {
        var rule = Rule("Memo Hint", CatFood, field: AutoCategorizationMatchField.AnyText);
        Assert.Equal(CatFood,
            AutoCategorizationEngine.Match(ImportCtx(counterparty: "Vendor", memo: "Memo Hint #1"), new[] { rule }));
    }

    [Fact]
    public void Match_ReturnsNull_WhenNoteIsNullOrWhitespace()
    {
        var rules = new[] { Rule("food", CatFood) };
        Assert.Null(AutoCategorizationEngine.Match((string?)null, rules));
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
