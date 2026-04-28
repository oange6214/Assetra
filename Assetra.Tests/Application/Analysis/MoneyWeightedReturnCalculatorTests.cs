using Assetra.Application.Analysis;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models;
using Assetra.Core.Models.Analysis;
using Xunit;

namespace Assetra.Tests.Application.Analysis;

public class MoneyWeightedReturnCalculatorTests
{
    [Fact]
    public async Task ComputeForEntryAsync_SingleCurrency_DegradesGracefully()
    {
        var entryId = Guid.NewGuid();
        var trades = new FakeTradeRepo();
        trades.Store.Add(MakeBuy(entryId, new DateTime(2026, 1, 5), 10, 100m));
        trades.Store.Add(MakeSell(entryId, new DateTime(2026, 4, 5), 10, 110m));

        var sut = new MoneyWeightedReturnCalculator(trades, new FakeSnapshotRepo(), new XirrCalculator());
        var period = new PerformancePeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 30));
        var irr = await sut.ComputeForEntryAsync(entryId, period);

        Assert.NotNull(irr);
        Assert.True(irr!.Value > 0m);
    }

    [Fact]
    public async Task ComputeForEntryAsync_CrossCurrency_ConvertsFlowsBeforeXirr()
    {
        var entryId = Guid.NewGuid();
        var port = new FakePortfolioRepo();
        port.Entries.Add(new PortfolioEntry(entryId, "AAPL", "NASDAQ", AssetType.Stock, "Apple", "USD"));

        var trades = new FakeTradeRepo();
        trades.Store.Add(MakeBuy(entryId, new DateTime(2026, 1, 5), 10, 100m));   // -1000 USD
        trades.Store.Add(MakeSell(entryId, new DateTime(2026, 4, 5), 10, 110m));  // +1100 USD

        var fx = new StubFx(("USD", "TWD", 32m));
        var settings = new StubSettings("TWD");

        var sut = new MoneyWeightedReturnCalculator(
            trades, new FakeSnapshotRepo(), new XirrCalculator(), port, fx, settings);
        var period = new PerformancePeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 30));
        var irr = await sut.ComputeForEntryAsync(entryId, period);

        Assert.NotNull(irr);
        Assert.True(irr!.Value > 0m);
    }

    [Fact]
    public async Task ComputeForEntryAsync_CrossCurrency_MissingRate_ReturnsNull()
    {
        var entryId = Guid.NewGuid();
        var port = new FakePortfolioRepo();
        port.Entries.Add(new PortfolioEntry(entryId, "7203", "TSE", AssetType.Stock, "Toyota", "JPY"));

        var trades = new FakeTradeRepo();
        trades.Store.Add(MakeBuy(entryId, new DateTime(2026, 1, 5), 1, 1000m));
        trades.Store.Add(MakeSell(entryId, new DateTime(2026, 4, 5), 1, 1100m));

        var fx = new StubFx(); // no JPY rate
        var settings = new StubSettings("TWD");

        var sut = new MoneyWeightedReturnCalculator(
            trades, new FakeSnapshotRepo(), new XirrCalculator(), port, fx, settings);
        var period = new PerformancePeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 30));
        var irr = await sut.ComputeForEntryAsync(entryId, period);

        Assert.Null(irr);
    }

    [Fact]
    public async Task ComputeAsync_SnapshotInForeignCurrency_ConvertsViaFx()
    {
        // Mixed scenario: TWD entry/trades, but legacy USD-tagged snapshots
        // (simulates: user previously had base=USD, switched to base=TWD).
        // Since IRR is scale-invariant, this proves the snapshot conversion *path* matters
        // by mixing the unit of trade flows (TWD) vs snapshots (USD requiring conversion).
        var entryId = Guid.NewGuid();
        var port = new FakePortfolioRepo();
        port.Entries.Add(new PortfolioEntry(entryId, "2330", "TWSE", AssetType.Stock, "TSMC", "TWD"));

        var trades = new FakeTradeRepo();
        trades.Store.Add(MakeBuy(entryId, new DateTime(2026, 1, 5), 10, 100m));   // -1000 TWD
        trades.Store.Add(MakeSell(entryId, new DateTime(2026, 4, 5), 10, 110m));  // +1100 TWD

        var snapshots = new FakeSnapshotRepo();
        snapshots.Store[new DateOnly(2026, 1, 1)] = new PortfolioDailySnapshot(
            new DateOnly(2026, 1, 1), 30m, 30m, 0m, 1, "USD");
        snapshots.Store[new DateOnly(2026, 4, 30)] = new PortfolioDailySnapshot(
            new DateOnly(2026, 4, 30), 30m, 35m, 5m, 1, "USD");

        var fx = new StubFx(("USD", "TWD", 32m));
        var settings = new StubSettings("TWD");

        var sut = new MoneyWeightedReturnCalculator(
            trades, snapshots, new XirrCalculator(), port, fx, settings);
        var period = new PerformancePeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 30));
        var irr = await sut.ComputeAsync(period);

        Assert.NotNull(irr);

        // Sanity: different rate → snapshot magnitudes change, trade flows unchanged → different IRR.
        var fx2 = new StubFx(("USD", "TWD", 16m));
        var sut2 = new MoneyWeightedReturnCalculator(
            trades, snapshots, new XirrCalculator(), port, fx2, settings);
        var irr2 = await sut2.ComputeAsync(period);
        Assert.NotNull(irr2);
        Assert.NotEqual(irr!.Value, irr2!.Value);
    }

    [Fact]
    public async Task ComputeAsync_SnapshotMissingFxRate_ReturnsNull()
    {
        var entryId = Guid.NewGuid();
        var port = new FakePortfolioRepo();
        port.Entries.Add(new PortfolioEntry(entryId, "X", "TWSE", AssetType.Stock, "X", "TWD"));

        var trades = new FakeTradeRepo();
        trades.Store.Add(MakeBuy(entryId, new DateTime(2026, 1, 5), 10, 100m));

        var snapshots = new FakeSnapshotRepo();
        snapshots.Store[new DateOnly(2026, 1, 1)] = new PortfolioDailySnapshot(
            new DateOnly(2026, 1, 1), 1000m, 1000m, 0m, 1, "JPY"); // missing rate

        var fx = new StubFx(); // no rates
        var settings = new StubSettings("TWD");

        var sut = new MoneyWeightedReturnCalculator(
            trades, snapshots, new XirrCalculator(), port, fx, settings);
        var period = new PerformancePeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 30));
        var irr = await sut.ComputeAsync(period);

        Assert.Null(irr);
    }

    [Fact]
    public async Task ComputeAsync_SnapshotMatchesBaseCurrency_NoConversionAttempted()
    {
        var entryId = Guid.NewGuid();
        var trades = new FakeTradeRepo();
        trades.Store.Add(MakeBuy(entryId, new DateTime(2026, 1, 5), 10, 100m));

        var snapshots = new FakeSnapshotRepo();
        snapshots.Store[new DateOnly(2026, 1, 1)] = new PortfolioDailySnapshot(
            new DateOnly(2026, 1, 1), 1000m, 1000m, 0m, 1, "TWD");
        snapshots.Store[new DateOnly(2026, 4, 30)] = new PortfolioDailySnapshot(
            new DateOnly(2026, 4, 30), 1000m, 1100m, 100m, 1, "TWD");

        // Even with FX configured, matching currency should not require any rate lookup.
        var port = new FakePortfolioRepo();
        var fx = new StubFx(); // empty — would fail if asked to convert
        var settings = new StubSettings("TWD");

        var sut = new MoneyWeightedReturnCalculator(
            trades, snapshots, new XirrCalculator(), port, fx, settings);
        var period = new PerformancePeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 4, 30));
        var irr = await sut.ComputeAsync(period);

        Assert.NotNull(irr);
    }

    private static Trade MakeBuy(Guid entryId, DateTime when, int qty, decimal price) => new(
        Guid.NewGuid(), "X", "TW", "X", TradeType.Buy, when, price, qty, null, null,
        PortfolioEntryId: entryId);

    private static Trade MakeSell(Guid entryId, DateTime when, int qty, decimal price) => new(
        Guid.NewGuid(), "X", "TW", "X", TradeType.Sell, when, price, qty, null, null,
        PortfolioEntryId: entryId);

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
            return Task.FromResult<decimal?>(_rates.TryGetValue((from.ToUpperInvariant(), to.ToUpperInvariant()), out var r) ? amount * r : null);
        }
    }

    private sealed class StubSettings : IAppSettingsService
    {
        public StubSettings(string baseCcy) { Current = new AppSettings { BaseCurrency = baseCcy }; }
        public AppSettings Current { get; private set; }
        public Task SaveAsync(AppSettings settings) { Current = settings; return Task.CompletedTask; }
        public event Action? Changed { add { } remove { } }
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

    private sealed class FakeSnapshotRepo : IPortfolioSnapshotRepository
    {
        public Dictionary<DateOnly, PortfolioDailySnapshot> Store { get; } = new();
        public Task<IReadOnlyList<PortfolioDailySnapshot>> GetSnapshotsAsync(DateOnly? from = null, DateOnly? to = null) =>
            Task.FromResult<IReadOnlyList<PortfolioDailySnapshot>>(Store.Values.OrderBy(s => s.SnapshotDate).ToList());
        public Task<PortfolioDailySnapshot?> GetSnapshotAsync(DateOnly date) =>
            Task.FromResult(Store.TryGetValue(date, out var s) ? s : null);
        public Task UpsertAsync(PortfolioDailySnapshot snapshot)
        {
            Store[snapshot.SnapshotDate] = snapshot;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTradeRepo : ITradeRepository
    {
        public List<Trade> Store { get; } = new();
        public Task<IReadOnlyList<Trade>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Trade>>(Store.ToList());
        public Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string l, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task<Trade?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Trade?>(Store.FirstOrDefault(t => t.Id == id));
        public Task AddAsync(Trade t, CancellationToken ct = default) { Store.Add(t); return Task.CompletedTask; }
        public Task UpdateAsync(Trade t, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveChildrenAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByAccountIdAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByLiabilityAsync(Guid? id, string? l, CancellationToken ct = default) => Task.CompletedTask;
        public Task ApplyAtomicAsync(IReadOnlyList<TradeMutation> mutations, CancellationToken ct = default) => Task.CompletedTask;
    }
}
