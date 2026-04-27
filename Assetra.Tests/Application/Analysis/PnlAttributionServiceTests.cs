using Assetra.Application.Analysis;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Core.Models.Analysis;
using Xunit;

namespace Assetra.Tests.Application.Analysis;

public class PnlAttributionServiceTests
{
    [Fact]
    public async Task ComputeAsync_AggregatesRealizedDividendCommission()
    {
        var trades = new FakeTradeRepo();
        // Sell with realized 500
        trades.Store.Add(new Trade(Guid.NewGuid(), "2330", "TW", "TSMC",
            TradeType.Sell, new DateTime(2026, 4, 5), 600m, 10, 500m, null,
            Commission: 50m));
        // Cash dividend 200
        trades.Store.Add(new Trade(Guid.NewGuid(), "2330", "TW", "TSMC",
            TradeType.CashDividend, new DateTime(2026, 4, 10), 2m, 100, null, null,
            CashAmount: 200m));
        // Buy with commission 30 (no realized)
        trades.Store.Add(new Trade(Guid.NewGuid(), "0050", "TW", "Yuanta",
            TradeType.Buy, new DateTime(2026, 4, 12), 100m, 10, null, null,
            Commission: 30m));

        var snaps = new FakeSnapshotRepo();
        var svc = new PnlAttributionService(trades, snaps);
        var period = PerformancePeriod.Month(2026, 4);
        var buckets = await svc.ComputeAsync(period);

        Assert.Equal(500m, buckets.Single(b => b.Label == "Realized").Amount);
        Assert.Equal(200m, buckets.Single(b => b.Label == "Dividend").Amount);
        Assert.Equal(-80m, buckets.Single(b => b.Label == "Commission").Amount);
        Assert.DoesNotContain(buckets, b => b.Label == "Unrealized Δ");
    }

    [Fact]
    public async Task ComputeAsync_WithSnapshots_AddsUnrealizedDelta()
    {
        var trades = new FakeTradeRepo();
        trades.Store.Add(new Trade(Guid.NewGuid(), "0050", "TW", "Yuanta",
            TradeType.Buy, new DateTime(2026, 4, 5), 100m, 10, null, null));
        var snaps = new FakeSnapshotRepo
        {
            Store =
            {
                [new DateOnly(2026, 4, 1)] = new PortfolioDailySnapshot(new DateOnly(2026, 4, 1), 5000m, 5000m, 0m, 1),
                [new DateOnly(2026, 4, 30)] = new PortfolioDailySnapshot(new DateOnly(2026, 4, 30), 6000m, 6500m, 500m, 2),
            },
        };
        var svc = new PnlAttributionService(trades, snaps);
        var buckets = await svc.ComputeAsync(PerformancePeriod.Month(2026, 4));

        // delta = 6500 - 5000 - 1000 (net invested via Buy 10*100) = 500
        var unr = buckets.Single(b => b.Label == "Unrealized Δ");
        Assert.Equal(500m, unr.Amount);
    }

    [Fact]
    public async Task ComputeAsync_OutOfPeriod_Excluded()
    {
        var trades = new FakeTradeRepo();
        trades.Store.Add(new Trade(Guid.NewGuid(), "X", "TW", "x",
            TradeType.Sell, new DateTime(2026, 3, 31), 0m, 0, 9999m, null));
        var svc = new PnlAttributionService(trades, new FakeSnapshotRepo());
        var buckets = await svc.ComputeAsync(PerformancePeriod.Month(2026, 4));
        Assert.Equal(0m, buckets.Single(b => b.Label == "Realized").Amount);
    }

    private sealed class FakeTradeRepo : ITradeRepository
    {
        public List<Trade> Store { get; } = new();
        public Task<IReadOnlyList<Trade>> GetAllAsync() => Task.FromResult<IReadOnlyList<Trade>>(Store.ToList());
        public Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string l) => Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid id) => Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task<Trade?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Trade?>(null);
        public Task AddAsync(Trade t) { Store.Add(t); return Task.CompletedTask; }
        public Task UpdateAsync(Trade t) => Task.CompletedTask;
        public Task RemoveAsync(Guid id) => Task.CompletedTask;
        public Task RemoveChildrenAsync(Guid id) => Task.CompletedTask;
        public Task RemoveByAccountIdAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByLiabilityAsync(Guid? id, string? l, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeSnapshotRepo : IPortfolioSnapshotRepository
    {
        public Dictionary<DateOnly, PortfolioDailySnapshot> Store { get; } = new();
        public Task<IReadOnlyList<PortfolioDailySnapshot>> GetSnapshotsAsync(DateOnly? from = null, DateOnly? to = null) =>
            Task.FromResult<IReadOnlyList<PortfolioDailySnapshot>>(Store.Values.ToList());
        public Task<PortfolioDailySnapshot?> GetSnapshotAsync(DateOnly date) =>
            Task.FromResult(Store.TryGetValue(date, out var s) ? s : null);
        public Task UpsertAsync(PortfolioDailySnapshot snapshot)
        {
            Store[snapshot.SnapshotDate] = snapshot;
            return Task.CompletedTask;
        }
    }
}
