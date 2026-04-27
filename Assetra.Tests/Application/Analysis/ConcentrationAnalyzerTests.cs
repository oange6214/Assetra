using Assetra.Application.Analysis;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Application.Analysis;

public class ConcentrationAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_EmptyPortfolio_ReturnsEmpty()
    {
        var svc = new ConcentrationAnalyzer(new FakePortfolioRepo(), new FakePositionQuery());
        var result = await svc.AnalyzeAsync();
        Assert.Empty(result);
    }

    [Fact]
    public async Task AnalyzeAsync_TopNAndOthers_AreComputedFromCostBasis()
    {
        var (port, query) = BuildFixture(new[]
        {
            ("A", 1000m), ("B", 2000m), ("C", 500m), ("D", 300m), ("E", 200m), ("F", 100m), ("G", 50m),
        });
        var svc = new ConcentrationAnalyzer(port, query);
        var result = await svc.AnalyzeAsync(topN: 5);

        Assert.Equal(6, result.Count); // 5 top + Others
        Assert.Equal("Others", result[5].Label);
        // total = 4150; B=2000 → ~0.4819
        var b = result.Single(r => r.Label.StartsWith("B"));
        Assert.InRange((double)b.Weight, 0.481, 0.483);
        // sum of weights ≈ 1
        Assert.InRange((double)result.Sum(r => r.Weight), 0.999, 1.001);
    }

    [Fact]
    public async Task ComputeHhiAsync_HighlyConcentrated_ReturnsHigh()
    {
        // Single position dominating → HHI close to 1
        var (port, query) = BuildFixture(new[] { ("A", 1000m), ("B", 10m) });
        var svc = new ConcentrationAnalyzer(port, query);
        var hhi = await svc.ComputeHhiAsync();
        Assert.NotNull(hhi);
        Assert.True(hhi!.Value > 0.9m);
    }

    private static (FakePortfolioRepo, FakePositionQuery) BuildFixture(
        IEnumerable<(string Symbol, decimal TotalCost)> rows)
    {
        var port = new FakePortfolioRepo();
        var query = new FakePositionQuery();
        foreach (var (sym, cost) in rows)
        {
            var id = Guid.NewGuid();
            port.Entries.Add(new PortfolioEntry(id, sym, "TW"));
            query.Snapshots[id] = new PositionSnapshot(id, 1m, cost, cost, 0m, null);
        }
        return (port, query);
    }

    private sealed class FakePortfolioRepo : IPortfolioRepository
    {
        public List<PortfolioEntry> Entries { get; } = new();
        public Task<IReadOnlyList<PortfolioEntry>> GetEntriesAsync() => Task.FromResult<IReadOnlyList<PortfolioEntry>>(Entries.ToList());
        public Task<IReadOnlyList<PortfolioEntry>> GetActiveAsync() => Task.FromResult<IReadOnlyList<PortfolioEntry>>(Entries.Where(e => e.IsActive).ToList());
        public Task AddAsync(PortfolioEntry entry) { Entries.Add(entry); return Task.CompletedTask; }
        public Task UpdateAsync(PortfolioEntry entry) => Task.CompletedTask;
        public Task UpdateMetadataAsync(Guid id, string displayName, string currency) => Task.CompletedTask;
        public Task RemoveAsync(Guid id) => Task.CompletedTask;
        public Task<Guid> FindOrCreatePortfolioEntryAsync(string symbol, string exchange, string? displayName, AssetType assetType, CancellationToken ct = default) => Task.FromResult(Guid.Empty);
        public Task ArchiveAsync(Guid id) => Task.CompletedTask;
        public Task UnarchiveAsync(Guid id) => Task.CompletedTask;
        public Task<int> HasTradeReferencesAsync(Guid id, CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakePositionQuery : IPositionQueryService
    {
        public Dictionary<Guid, PositionSnapshot> Snapshots { get; } = new();
        public Task<PositionSnapshot?> GetPositionAsync(Guid portfolioEntryId) =>
            Task.FromResult(Snapshots.TryGetValue(portfolioEntryId, out var s) ? s : null);
        public Task<IReadOnlyDictionary<Guid, PositionSnapshot>> GetAllPositionSnapshotsAsync() =>
            Task.FromResult<IReadOnlyDictionary<Guid, PositionSnapshot>>(Snapshots);
        public Task<decimal> ComputeRealizedPnlAsync(Guid portfolioEntryId, DateTime sellDate, decimal sellPrice, decimal sellQty, decimal sellFees) => Task.FromResult(0m);
    }
}
