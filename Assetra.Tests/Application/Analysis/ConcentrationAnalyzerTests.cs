using Assetra.Application.Analysis;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
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

    [Fact]
    public async Task AnalyzeAsync_CrossCurrency_ConvertsCostBeforeAggregation()
    {
        var port = new FakePortfolioRepo();
        var query = new FakePositionQuery();

        var twId = Guid.NewGuid();
        var usId = Guid.NewGuid();
        port.Entries.Add(new PortfolioEntry(twId, "2330", "TW", AssetType.Stock, "TSMC", "TWD"));
        port.Entries.Add(new PortfolioEntry(usId, "AAPL", "NASDAQ", AssetType.Stock, "Apple", "USD"));
        query.Snapshots[twId] = new PositionSnapshot(twId, 1m, 32_000m, 32_000m, 0m, null);
        query.Snapshots[usId] = new PositionSnapshot(usId, 1m, 1_000m, 1_000m, 0m, null);

        var fx = new StubFx(("USD", "TWD", 32m));
        var settings = new StubSettings("TWD");

        var svc = new ConcentrationAnalyzer(port, query, fx, settings);
        var result = await svc.AnalyzeAsync(topN: 5);

        Assert.Equal(2, result.Count);
        Assert.InRange((double)result.Sum(r => r.Weight), 0.999, 1.001);
        // After conversion both legs are 32000 TWD → 50/50
        Assert.InRange((double)result[0].Weight, 0.499, 0.501);
    }

    [Fact]
    public async Task AnalyzeAsync_CrossCurrency_MissingRate_SkipsBucket()
    {
        var port = new FakePortfolioRepo();
        var query = new FakePositionQuery();

        var twId = Guid.NewGuid();
        var jpId = Guid.NewGuid();
        port.Entries.Add(new PortfolioEntry(twId, "2330", "TW", AssetType.Stock, "TSMC", "TWD"));
        port.Entries.Add(new PortfolioEntry(jpId, "7203", "TSE", AssetType.Stock, "Toyota", "JPY"));
        query.Snapshots[twId] = new PositionSnapshot(twId, 1m, 1000m, 1000m, 0m, null);
        query.Snapshots[jpId] = new PositionSnapshot(jpId, 1m, 5000m, 5000m, 0m, null);

        var fx = new StubFx(); // no rates
        var settings = new StubSettings("TWD");

        var svc = new ConcentrationAnalyzer(port, query, fx, settings);
        var result = await svc.AnalyzeAsync(topN: 5);

        Assert.Single(result);
        Assert.StartsWith("2330", result[0].Label);
    }

    private sealed class StubFx : IMultiCurrencyValuationService
    {
        private readonly Dictionary<(string, string), decimal> _rates;
        public StubFx(params (string From, string To, decimal Rate)[] rates)
        {
            _rates = rates.ToDictionary(r => (r.From.ToUpperInvariant(), r.To.ToUpperInvariant()), r => r.Rate);
        }
        public Task<decimal?> ConvertAsync(decimal amount, string from, string to, DateOnly asOf, CancellationToken ct = default)
        {
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return Task.FromResult<decimal?>(amount);
            if (_rates.TryGetValue((from.ToUpperInvariant(), to.ToUpperInvariant()), out var r))
                return Task.FromResult<decimal?>(amount * r);
            return Task.FromResult<decimal?>(null);
        }
    }

    private sealed class StubSettings : IAppSettingsService
    {
        public StubSettings(string baseCcy) { Current = new AppSettings { BaseCurrency = baseCcy }; }
        public AppSettings Current { get; private set; }
        public Task SaveAsync(AppSettings settings) { Current = settings; return Task.CompletedTask; }
        public event Action? Changed { add { } remove { } }
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
        public Task<IReadOnlyList<PortfolioEntry>> GetEntriesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PortfolioEntry>>(Entries.ToList());
        public Task<IReadOnlyList<PortfolioEntry>> GetActiveAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PortfolioEntry>>(Entries.Where(e => e.IsActive).ToList());
        public Task AddAsync(PortfolioEntry entry, CancellationToken ct = default) { Entries.Add(entry); return Task.CompletedTask; }
        public Task UpdateAsync(PortfolioEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateMetadataAsync(Guid id, string displayName, string currency, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Guid> FindOrCreatePortfolioEntryAsync(string symbol, string exchange, string? displayName, AssetType assetType, CancellationToken ct = default) => Task.FromResult(Guid.Empty);
        public Task ArchiveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task UnarchiveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
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
