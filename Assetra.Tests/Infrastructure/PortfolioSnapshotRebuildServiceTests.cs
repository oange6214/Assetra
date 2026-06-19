using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Xunit;

namespace Assetra.Tests.Infrastructure;

/// <summary>
/// Stage-1 coverage for <see cref="PortfolioSnapshotRebuildService"/> — the FULL-breakdown
/// historical reconstruction engine.
///
/// <para>
/// Unlike <see cref="PortfolioBackfillService"/> (market value only, single currency), this
/// service must reproduce the exact numbers the live <c>PortfolioViewModel</c> writes:
/// equity = Σ close×qty converted to base via as-of-D FX, plus cash and liability pulled from
/// the as-of-D balance projections and likewise FX-converted. The tests below pin the three
/// behaviours that make this safe to run against real history:
/// </para>
/// <list type="number">
///   <item>multi-currency equity sums correctly through as-of-D FX (the headline value);</item>
///   <item>all-or-nothing — a single missing close OR a single missing rate skips the whole day,
///         never a partial sum (the Q09 discipline, extended to FX);</item>
///   <item>live rows (snapshots already carrying a breakdown) are never overwritten; and</item>
///   <item>dry-run computes the same values but writes nothing.</item>
/// </list>
/// </summary>
public sealed class PortfolioSnapshotRebuildServiceTests
{
    // A single weekday with no existing snapshot. Logs are dated earlier so the position is
    // active on D (ReconstructPositions keeps latest log ≤ D with qty > 0).
    private static readonly DateOnly Day = new(2024, 1, 10);   // Wednesday
    private static readonly DateOnly LogDay = new(2024, 1, 2); // earlier weekday

    private const string Base = "TWD"; // _settings is null in tests → base = IBalanceQueryService.DefaultCurrency
    private const decimal UsdToTwd = 32m;

    // ─── 1. Multi-currency equity — THE key test ─────────────────────────────

    [Fact]
    public async Task Rebuild_MultiCurrencyEquity_SumsConvertedClosesAndBalances()
    {
        // WHY: this is the whole reason the engine exists. The live path values each position in
        // ITS OWN currency (row.Currency = entry.Currency) and converts to base via as-of-D FX.
        // A TWD leg passes through at 1:1; a USD leg must be multiplied by the as-of-D USD→TWD
        // rate. If the engine summed raw closes (ignoring currency) the USD leg would be ~32×
        // understated — exactly the kind of silent corruption these snapshots feed into the
        // calendar / trends. We also prove cash + liability come from the faked as-of balances,
        // each FX-converted, so the breakdown is internally consistent.
        var twdPos = Log("2330", "TWSE", qty: 10, buyPrice: 500m);
        var usdPos = Log("AAPL", "NASDAQ", qty: 4, buyPrice: 150m);
        var logRepo = new FakeLogRepository(twdPos, usdPos);

        var snapshots = new RecordingSnapshotRepository();
        var history = new FakeHistoryProvider();
        history.Add("2330", Day, close: 600m);   // TWD close
        history.Add("AAPL", Day, close: 200m);    // USD close

        var fx = new FakeFx();
        // identity for base; fixed USD→TWD rate on D.
        var portfolio = new FakePortfolioRepository(
            Entry("2330", "TWSE", "TWD"),
            Entry("AAPL", "NASDAQ", "USD"));

        // Cash: 100,000 TWD + 1,000 USD ; Liability: 2,000 USD loan.
        var balances = new FakeBalanceQueryService();
        balances.Cash[Guid.NewGuid()] = new Money(100_000m, "TWD");
        balances.Cash[Guid.NewGuid()] = new Money(1_000m, "USD");
        balances.Liabilities["card"] = new LiabilitySnapshot(new Money(2_000m, "USD"), new Money(2_000m, "USD"));

        var service = new PortfolioSnapshotRebuildService(logRepo, snapshots, history, fx, balances, portfolio);

        var report = await service.RebuildAsync(Day, Day, dryRun: false);

        var written = Assert.Single(snapshots.Writes);
        var expectedEquity = (600m * 10) + (200m * 4 * UsdToTwd);          // 6,000 + 25,600 = 31,600
        var expectedCost = (500m * 10) + (150m * 4 * UsdToTwd);            // 5,000 + 19,200 = 24,200
        var expectedCash = 100_000m + (1_000m * UsdToTwd);                  // 100,000 + 32,000 = 132,000
        var expectedLiability = 2_000m * UsdToTwd;                          // 64,000

        Assert.Equal(expectedEquity, written.MarketValue);
        Assert.Equal(expectedEquity, written.EquityValue);
        Assert.Equal(expectedCash, written.CashValue);
        Assert.Equal(expectedLiability, written.LiabilityValue);
        Assert.Equal(expectedCost, written.TotalCost);
        Assert.Equal(expectedEquity - expectedCost, written.Pnl);
        Assert.Equal(2, written.PositionCount);
        Assert.Equal(Base, written.Currency);

        var day = Assert.Single(report.Days);
        Assert.Equal(RebuildDayStatus.Rebuilt, day.Status);
        Assert.Equal(1, report.RebuiltCount);
        // Report mirrors the persisted values.
        Assert.Equal(expectedEquity, day.NewMarketValue);
        Assert.Equal(expectedCash, day.NewCash);
        Assert.Equal(expectedLiability, day.NewLiability);
    }

    // ─── 2. Skip when FX rate is missing ─────────────────────────────────────

    [Fact]
    public async Task Rebuild_MissingFxRate_SkipsDayAndWritesNothing()
    {
        // WHY: a position whose currency→base rate is unknown for D cannot be valued in base. The
        // all-or-nothing rule must reject the WHOLE day (status NoFx) rather than persist a
        // base-currency sum that silently treats the foreign amount as TWD. No upsert may reach the
        // repo. EUR is used because FakeFx has NO EUR→TWD path → ConvertAsync returns null.
        var logRepo = new FakeLogRepository(Log("BMW", "XETRA", qty: 4, buyPrice: 150m));
        var snapshots = new RecordingSnapshotRepository();
        var history = new FakeHistoryProvider();
        history.Add("BMW", Day, close: 200m);

        var fx = new FakeFx();
        var portfolio = new FakePortfolioRepository(Entry("BMW", "XETRA", "EUR"));
        var balances = new FakeBalanceQueryService();

        var service = new PortfolioSnapshotRebuildService(logRepo, snapshots, history, fx, balances, portfolio);

        var report = await service.RebuildAsync(Day, Day, dryRun: false);

        Assert.Empty(snapshots.Writes);
        var day = Assert.Single(report.Days);
        Assert.Equal(RebuildDayStatus.SkippedNoFx, day.Status);
        Assert.Equal(1, report.NoFxCount);
        Assert.Equal(0, report.RebuiltCount);
    }

    // ─── 3. Skip when a position is unpriceable ──────────────────────────────

    [Fact]
    public async Task Rebuild_PositionMissingClose_SkipsDayAndWritesNothing()
    {
        // WHY: all-or-nothing on price too — if even one held position has no historical close on
        // D, the equity sum would be partial while PositionCount counted every position. That is
        // precisely the Q09 garbage-snapshot bug; the engine must skip the day entirely.
        var logRepo = new FakeLogRepository(
            Log("2330", "TWSE", qty: 10, buyPrice: 500m),
            Log("2317", "TWSE", qty: 20, buyPrice: 100m));
        var snapshots = new RecordingSnapshotRepository();
        var history = new FakeHistoryProvider();
        history.Add("2330", Day, close: 600m); // 2317 deliberately omitted

        var fx = new FakeFx();
        var portfolio = new FakePortfolioRepository(
            Entry("2330", "TWSE", "TWD"),
            Entry("2317", "TWSE", "TWD"));
        var balances = new FakeBalanceQueryService();

        var service = new PortfolioSnapshotRebuildService(logRepo, snapshots, history, fx, balances, portfolio);

        var report = await service.RebuildAsync(Day, Day, dryRun: false);

        Assert.Empty(snapshots.Writes);
        var day = Assert.Single(report.Days);
        Assert.Equal(RebuildDayStatus.SkippedUnpriceable, day.Status);
        Assert.Equal(1, report.UnpriceableCount);
    }

    // ─── 4. Preserve an existing live row ────────────────────────────────────

    [Fact]
    public async Task Rebuild_ExistingLiveRowWithBreakdown_IsPreservedNotOverwritten()
    {
        // WHY: the live PortfolioViewModel already writes a complete breakdown for "today" (and
        // recent days). The migration runner must NEVER clobber those authoritative rows with a
        // reconstructed approximation. Any existing snapshot carrying a breakdown component
        // (cash/equity/liability non-null) is treated as live and left untouched.
        var logRepo = new FakeLogRepository(Log("2330", "TWSE", qty: 10, buyPrice: 500m));
        var snapshots = new RecordingSnapshotRepository();
        snapshots.Seed(new PortfolioDailySnapshot(
            Day, TotalCost: 5_000m, MarketValue: 6_000m, Pnl: 1_000m, PositionCount: 1,
            Currency: Base, CashValue: 1_000m, EquityValue: 6_000m, LiabilityValue: 0m));
        var history = new FakeHistoryProvider();
        history.Add("2330", Day, close: 999m); // would change the value if it were (wrongly) recomputed

        var fx = new FakeFx();
        var portfolio = new FakePortfolioRepository(Entry("2330", "TWSE", "TWD"));
        var balances = new FakeBalanceQueryService();

        var service = new PortfolioSnapshotRebuildService(logRepo, snapshots, history, fx, balances, portfolio);

        var report = await service.RebuildAsync(Day, Day, dryRun: false);

        Assert.Empty(snapshots.Writes); // no upsert at all
        var day = Assert.Single(report.Days);
        Assert.Equal(RebuildDayStatus.SkippedHasCompleteLiveRow, day.Status);
        Assert.Equal(1, report.PreservedCount);
        // OldMarketValue carries the preserved row's value through to the report.
        Assert.Equal(6_000m, day.OldMarketValue);
    }

    [Fact]
    public async Task Rebuild_OverwriteLive_RecomputesExistingLiveRowInsteadOfPreserving()
    {
        // WHY: the manual「重建快照」must actually overwrite existing live rows (overwriteLive:true).
        // Otherwise — when every recent day already has a live breakdown snapshot — the rebuild
        // reports "0 days" and silently does nothing (the user's "按了沒反應"). With the flag set, a
        // live row is recomputed from historical close, not preserved.
        var logRepo = new FakeLogRepository(Log("2330", "TWSE", qty: 10, buyPrice: 500m));
        var snapshots = new RecordingSnapshotRepository();
        snapshots.Seed(new PortfolioDailySnapshot(
            Day, TotalCost: 5_000m, MarketValue: 6_000m, Pnl: 1_000m, PositionCount: 1,
            Currency: Base, CashValue: 1_000m, EquityValue: 6_000m, LiabilityValue: 0m));
        var history = new FakeHistoryProvider();
        history.Add("2330", Day, close: 999m); // recompute → equity 9,990, overwriting the stale 6,000

        var fx = new FakeFx();
        var portfolio = new FakePortfolioRepository(Entry("2330", "TWSE", "TWD"));
        var balances = new FakeBalanceQueryService();

        var service = new PortfolioSnapshotRebuildService(logRepo, snapshots, history, fx, balances, portfolio);

        var report = await service.RebuildAsync(Day, Day, dryRun: false, overwriteLive: true);

        var written = Assert.Single(snapshots.Writes); // overwritten, NOT preserved
        var day = Assert.Single(report.Days);
        Assert.Equal(RebuildDayStatus.Rebuilt, day.Status);
        Assert.Equal(1, report.RebuiltCount);
        Assert.Equal(0, report.PreservedCount);
        Assert.Equal(999m * 10, written.EquityValue); // recomputed from close, not the stale 6,000
    }

    // ─── 5. dry-run computes values but writes nothing ───────────────────────

    [Fact]
    public async Task Rebuild_DryRun_ReportsValuesButPersistsNothing()
    {
        // WHY: dry-run is how the user previews a migration before committing. It must produce the
        // SAME computed values as a real run (so the preview is trustworthy) while the snapshot
        // repository receives ZERO upserts.
        var logRepo = new FakeLogRepository(Log("2330", "TWSE", qty: 10, buyPrice: 500m));
        var snapshots = new RecordingSnapshotRepository();
        var history = new FakeHistoryProvider();
        history.Add("2330", Day, close: 600m);

        var fx = new FakeFx();
        var portfolio = new FakePortfolioRepository(Entry("2330", "TWSE", "TWD"));
        var balances = new FakeBalanceQueryService();
        balances.Cash[Guid.NewGuid()] = new Money(7_000m, "TWD");

        var service = new PortfolioSnapshotRebuildService(logRepo, snapshots, history, fx, balances, portfolio);

        var report = await service.RebuildAsync(Day, Day, dryRun: true);

        Assert.Empty(snapshots.Writes); // nothing persisted in dry-run
        Assert.True(report.DryRun);
        var day = Assert.Single(report.Days);
        Assert.Equal(RebuildDayStatus.Rebuilt, day.Status); // still reported as a would-be rebuild
        Assert.Equal(6_000m, day.NewMarketValue);
        Assert.Equal(6_000m, day.NewEquity);
        Assert.Equal(7_000m, day.NewCash);
        Assert.Equal(0m, day.NewLiability);
    }

    // ─── 6. non-dry-run persists a full breakdown ────────────────────────────

    [Fact]
    public async Task Rebuild_NotDryRun_UpsertsSnapshotWithFullBreakdown()
    {
        // WHY: a real run must persist a row whose cash / equity / liability are ALL non-null —
        // that completeness is exactly what distinguishes a rebuilt row from a legacy market-value-
        // only row, and what the preserve-live-row guard keys on for subsequent runs.
        var logRepo = new FakeLogRepository(Log("2330", "TWSE", qty: 10, buyPrice: 500m));
        var snapshots = new RecordingSnapshotRepository();
        var history = new FakeHistoryProvider();
        history.Add("2330", Day, close: 600m);

        var fx = new FakeFx();
        var portfolio = new FakePortfolioRepository(Entry("2330", "TWSE", "TWD"));
        var balances = new FakeBalanceQueryService();
        balances.Cash[Guid.NewGuid()] = new Money(7_000m, "TWD");
        balances.Liabilities["loan"] = new LiabilitySnapshot(new Money(3_000m, "TWD"), new Money(3_000m, "TWD"));

        var service = new PortfolioSnapshotRebuildService(logRepo, snapshots, history, fx, balances, portfolio);

        await service.RebuildAsync(Day, Day, dryRun: false);

        var written = Assert.Single(snapshots.Writes);
        Assert.Equal(Day, written.SnapshotDate);
        Assert.NotNull(written.CashValue);
        Assert.NotNull(written.EquityValue);
        Assert.NotNull(written.LiabilityValue);
        Assert.Equal(6_000m, written.EquityValue);
        Assert.Equal(7_000m, written.CashValue);
        Assert.Equal(3_000m, written.LiabilityValue);
    }

    // ─── 7. cash & liability as-of-D are FX-converted and summed ─────────────

    [Fact]
    public async Task Rebuild_CashAndLiabilityInForeignCurrency_AreConvertedAndSummed()
    {
        // WHY: cash and liability follow the same as-of-D FX rule as equity. Multiple accounts in
        // mixed currencies must each be converted to base before summing — never added raw across
        // currencies (which Money's operators would even throw on). Here: (5,000 TWD + 100 USD)
        // cash and (10 USD + 1,000 TWD) liability, all at the fixed USD→TWD rate.
        var logRepo = new FakeLogRepository(Log("2330", "TWSE", qty: 1, buyPrice: 100m));
        var snapshots = new RecordingSnapshotRepository();
        var history = new FakeHistoryProvider();
        history.Add("2330", Day, close: 100m);

        var fx = new FakeFx();
        var portfolio = new FakePortfolioRepository(Entry("2330", "TWSE", "TWD"));
        var balances = new FakeBalanceQueryService();
        balances.Cash[Guid.NewGuid()] = new Money(5_000m, "TWD");
        balances.Cash[Guid.NewGuid()] = new Money(100m, "USD");
        balances.Liabilities["a"] = new LiabilitySnapshot(new Money(10m, "USD"), new Money(10m, "USD"));
        balances.Liabilities["b"] = new LiabilitySnapshot(new Money(1_000m, "TWD"), new Money(1_000m, "TWD"));

        var service = new PortfolioSnapshotRebuildService(logRepo, snapshots, history, fx, balances, portfolio);

        await service.RebuildAsync(Day, Day, dryRun: false);

        var written = Assert.Single(snapshots.Writes);
        Assert.Equal(5_000m + (100m * UsdToTwd), written.CashValue);          // 5,000 + 3,200 = 8,200
        Assert.Equal((10m * UsdToTwd) + 1_000m, written.LiabilityValue);      // 320 + 1,000 = 1,320
    }

    // ─── Builders ────────────────────────────────────────────────────────────

    private static PortfolioPositionLog Log(string symbol, string exchange, int qty, decimal buyPrice) =>
        new(Guid.NewGuid(), LogDay, Guid.NewGuid(), symbol, exchange, qty, buyPrice);

    private static PortfolioEntry Entry(string symbol, string exchange, string currency) =>
        new(Guid.NewGuid(), symbol, exchange, AssetType.Stock, DisplayName: symbol, Currency: currency);

    // ─── Fakes ─────────────────────────────────────────────────────────────

    private sealed class FakeLogRepository : IPortfolioPositionLogRepository
    {
        private readonly IReadOnlyList<PortfolioPositionLog> _logs;
        public FakeLogRepository(params PortfolioPositionLog[] logs) => _logs = logs;

        public Task<IReadOnlyList<PortfolioPositionLog>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult(_logs);
        public Task<bool> HasAnyAsync(CancellationToken ct = default) => Task.FromResult(_logs.Count > 0);
        public Task LogAsync(PortfolioPositionLog entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task LogBatchAsync(IEnumerable<PortfolioPositionLog> entries, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    /// <summary>
    /// Records upserts in <see cref="Writes"/> while keeping pre-seeded "existing" rows separate —
    /// so a seeded live row is visible to <see cref="GetSnapshotsAsync"/> (the preserve-row read)
    /// WITHOUT being counted as a write. This separation is what lets test 4 assert "zero upserts".
    /// </summary>
    private sealed class RecordingSnapshotRepository : IPortfolioSnapshotRepository
    {
        private readonly List<PortfolioDailySnapshot> _existing = [];
        public List<PortfolioDailySnapshot> Writes { get; } = [];

        public void Seed(PortfolioDailySnapshot snapshot) => _existing.Add(snapshot);

        public Task<IReadOnlyList<PortfolioDailySnapshot>> GetSnapshotsAsync(
            DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
        {
            IEnumerable<PortfolioDailySnapshot> q = _existing;
            if (from is { } f) q = q.Where(s => s.SnapshotDate >= f);
            if (to is { } t) q = q.Where(s => s.SnapshotDate <= t);
            return Task.FromResult<IReadOnlyList<PortfolioDailySnapshot>>(q.ToList());
        }

        public Task<PortfolioDailySnapshot?> GetSnapshotAsync(DateOnly date, CancellationToken ct = default) =>
            Task.FromResult(
                Writes.Concat(_existing).LastOrDefault(s => s.SnapshotDate == date));

        public Task UpsertAsync(PortfolioDailySnapshot snapshot, CancellationToken ct = default)
        {
            Writes.Add(snapshot);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHistoryProvider : IStockHistoryProvider
    {
        private readonly Dictionary<string, Dictionary<DateOnly, decimal>> _bySymbol = new(StringComparer.Ordinal);

        public void Add(string symbol, DateOnly date, decimal close)
        {
            if (!_bySymbol.TryGetValue(symbol, out var byDate))
            {
                byDate = new Dictionary<DateOnly, decimal>();
                _bySymbol[symbol] = byDate;
            }
            byDate[date] = close;
        }

        public Task<IReadOnlyList<OhlcvPoint>> GetHistoryAsync(
            string symbol, string exchange, ChartPeriod period, CancellationToken ct = default)
        {
            if (!_bySymbol.TryGetValue(symbol, out var byDate))
                return Task.FromResult<IReadOnlyList<OhlcvPoint>>(Array.Empty<OhlcvPoint>());
            IReadOnlyList<OhlcvPoint> points = byDate
                .Select(kvp => new OhlcvPoint(kvp.Key, kvp.Value, kvp.Value, kvp.Value, kvp.Value, 0L))
                .ToList();
            return Task.FromResult(points);
        }
    }

    /// <summary>
    /// FX fake: same-currency is 1:1; USD→TWD uses the fixed test rate; every other pair returns
    /// <see langword="null"/> (no rate). Tests simulate a missing rate by using a currency with no
    /// registered path — e.g. EUR in <see cref="Rebuild_MissingFxRate_SkipsDayAndWritesNothing"/>.
    /// </summary>
    private sealed class FakeFx : IMultiCurrencyValuationService
    {
        public Task<decimal?> ConvertAsync(
            decimal amount, string from, string to, DateOnly asOf, CancellationToken ct = default)
        {
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<decimal?>(amount);
            if (string.Equals(from, "USD", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(to, "TWD", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<decimal?>(amount * UsdToTwd);
            return Task.FromResult<decimal?>(null); // no rate → caller skips the day
        }
    }

    private sealed class FakePortfolioRepository : IPortfolioRepository
    {
        private readonly IReadOnlyList<PortfolioEntry> _entries;
        public FakePortfolioRepository(params PortfolioEntry[] entries) => _entries = entries;

        public Task<IReadOnlyList<PortfolioEntry>> GetEntriesAsync(CancellationToken ct = default) =>
            Task.FromResult(_entries);
        public Task<IReadOnlyList<PortfolioEntry>> GetActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PortfolioEntry>>(_entries.Where(e => e.IsActive).ToList());
        public Task AddAsync(PortfolioEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(PortfolioEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateMetadataAsync(Guid id, string displayName, string currency, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Guid> FindOrCreatePortfolioEntryAsync(
            string symbol, string exchange, string? displayName, AssetType assetType,
            string? currency = null, bool isEtf = false, Guid? portfolioGroupId = null,
            CancellationToken ct = default) => Task.FromResult(Guid.NewGuid());
        public Task ArchiveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task UnarchiveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> HasTradeReferencesAsync(Guid id, CancellationToken ct = default) => Task.FromResult(0);
    }

    /// <summary>
    /// Returns configured as-of-D cash / liability balances regardless of the as-of date (the date
    /// filtering itself is covered by <see cref="BalanceQueryServiceTests"/>; here we only need the
    /// rebuild service to consume whatever the projection yields). The non-as-of members throw — the
    /// rebuild service must use only the as-of-D variants.
    /// </summary>
    private sealed class FakeBalanceQueryService : IBalanceQueryService
    {
        public Dictionary<Guid, Money> Cash { get; } = new();
        public Dictionary<string, LiabilitySnapshot> Liabilities { get; } = new(StringComparer.Ordinal);

        public Task<IReadOnlyDictionary<Guid, Money>> GetAllCashBalancesAsOfAsync(
            DateOnly asOf, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, Money>>(Cash);

        public Task<IReadOnlyDictionary<string, LiabilitySnapshot>> GetAllLiabilitySnapshotsAsOfAsync(
            DateOnly asOf, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, LiabilitySnapshot>>(Liabilities);

        public Task<Money> GetCashBalanceAsync(Guid cashAccountId) => throw new NotSupportedException();
        public Task<LiabilitySnapshot> GetLiabilitySnapshotAsync(string loanLabel) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, Money>> GetAllCashBalancesAsync() => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, LiabilitySnapshot>> GetAllLiabilitySnapshotsAsync() =>
            throw new NotSupportedException();
    }
}
