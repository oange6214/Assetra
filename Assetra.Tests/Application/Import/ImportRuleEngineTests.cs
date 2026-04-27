using Assetra.Application.Import;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models.Import;
using Xunit;

namespace Assetra.Tests.Application.Import;

public class ImportRuleEngineTests
{
    private static ImportPreviewRow Row(string? counterparty = null, string? memo = null) =>
        new(1, new DateOnly(2026, 4, 27), -100m, counterparty, memo);

    private static ImportRule Rule(
        Guid categoryId,
        string pattern,
        ImportRuleMatchField field = ImportRuleMatchField.Counterparty,
        ImportRuleMatchType type = ImportRuleMatchType.Contains,
        bool caseSensitive = false,
        int priority = 0,
        bool enabled = true) =>
        new(
            Id: Guid.NewGuid(),
            Name: $"rule-{pattern}",
            MatchField: field,
            MatchType: type,
            Pattern: pattern,
            CaseSensitive: caseSensitive,
            CategoryId: categoryId,
            Priority: priority,
            IsEnabled: enabled,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

    private sealed class FakeRepo : IImportRuleRepository
    {
        public List<ImportRule> Store { get; } = new();
        public Task<IReadOnlyList<ImportRule>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ImportRule>>(Store.ToList());
        public Task<ImportRule?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Store.FirstOrDefault(r => r.Id == id));
        public Task AddAsync(ImportRule r, CancellationToken ct = default) { Store.Add(r); return Task.CompletedTask; }
        public Task UpdateAsync(ImportRule r, CancellationToken ct = default)
        {
            var idx = Store.FindIndex(x => x.Id == r.Id);
            if (idx >= 0) Store[idx] = r;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(Guid id, CancellationToken ct = default)
        {
            Store.RemoveAll(r => r.Id == id);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task TryResolveCategory_NoRules_ReturnsFalse()
    {
        var engine = new ImportRuleEngine(new FakeRepo());
        await engine.RefreshAsync();
        Assert.False(engine.TryResolveCategory(Row(counterparty: "Anything"), out var cid));
        Assert.Null(cid);
    }

    [Fact]
    public async Task TryResolveCategory_ContainsMatch_ReturnsCategoryId()
    {
        var cat = Guid.NewGuid();
        var repo = new FakeRepo();
        repo.Store.Add(Rule(cat, "Starbucks"));
        var engine = new ImportRuleEngine(repo);
        await engine.RefreshAsync();

        Assert.True(engine.TryResolveCategory(Row(counterparty: "STARBUCKS Taipei"), out var cid));
        Assert.Equal(cat, cid);
    }

    [Fact]
    public async Task TryResolveCategory_DisabledRule_Ignored()
    {
        var cat = Guid.NewGuid();
        var repo = new FakeRepo();
        repo.Store.Add(Rule(cat, "Starbucks", enabled: false));
        var engine = new ImportRuleEngine(repo);
        await engine.RefreshAsync();

        Assert.False(engine.TryResolveCategory(Row(counterparty: "Starbucks"), out _));
    }

    [Fact]
    public async Task TryResolveCategory_RespectsPriorityOrder()
    {
        var low = Guid.NewGuid();
        var high = Guid.NewGuid();
        var repo = new FakeRepo();
        repo.Store.Add(Rule(low, "Coffee", priority: 100));
        repo.Store.Add(Rule(high, "Coffee", priority: 1));
        var engine = new ImportRuleEngine(repo);
        await engine.RefreshAsync();

        Assert.True(engine.TryResolveCategory(Row(counterparty: "Coffee Bar"), out var cid));
        Assert.Equal(high, cid);
    }

    [Fact]
    public async Task TryResolveCategory_CaseSensitiveRule_OnlyMatchesExactCase()
    {
        var cat = Guid.NewGuid();
        var repo = new FakeRepo();
        repo.Store.Add(Rule(cat, "Pay", caseSensitive: true, type: ImportRuleMatchType.Equals));
        var engine = new ImportRuleEngine(repo);
        await engine.RefreshAsync();

        Assert.False(engine.TryResolveCategory(Row(counterparty: "pay"), out _));
        Assert.True(engine.TryResolveCategory(Row(counterparty: "Pay"), out var cid));
        Assert.Equal(cat, cid);
    }

    [Fact]
    public async Task TryResolveCategory_RegexAcrossEither_MatchesMemo()
    {
        var cat = Guid.NewGuid();
        var repo = new FakeRepo();
        repo.Store.Add(Rule(cat, @"^INV-\d+", field: ImportRuleMatchField.Either, type: ImportRuleMatchType.Regex));
        var engine = new ImportRuleEngine(repo);
        await engine.RefreshAsync();

        Assert.True(engine.TryResolveCategory(Row(counterparty: "Vendor", memo: "INV-1234"), out var cid));
        Assert.Equal(cat, cid);
    }

    [Fact]
    public async Task TryResolveCategory_BadRegex_IsSkipped()
    {
        var cat = Guid.NewGuid();
        var repo = new FakeRepo();
        repo.Store.Add(Rule(cat, "([unclosed", type: ImportRuleMatchType.Regex));
        var engine = new ImportRuleEngine(repo);
        await engine.RefreshAsync();

        Assert.False(engine.TryResolveCategory(Row(counterparty: "anything"), out _));
    }

    [Fact]
    public async Task RowMapper_WithEngine_AssignsCategoryToBankIncome()
    {
        var cat = Guid.NewGuid();
        var repo = new FakeRepo();
        repo.Store.Add(Rule(cat, "Salary"));
        var engine = new ImportRuleEngine(repo);
        await engine.RefreshAsync();

        var mapper = new ImportRowMapper(engine);
        var warnings = new List<string>();
        var trade = mapper.Map(
            new ImportPreviewRow(1, new DateOnly(2026, 4, 27), 5000m, "Salary April", null),
            ImportSourceKind.BankStatement,
            new ImportApplyOptions(),
            warnings);

        Assert.NotNull(trade);
        Assert.Equal(cat, trade!.CategoryId);
    }
}
