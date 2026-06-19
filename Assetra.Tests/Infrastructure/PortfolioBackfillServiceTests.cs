using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.Persistence;
using Xunit;

namespace Assetra.Tests.Infrastructure;

/// <summary>
/// Q09 regression coverage for <see cref="PortfolioBackfillService"/>.
/// <para>
/// The bug: both <see cref="PortfolioBackfillService.BackfillAsync"/> and
/// <see cref="PortfolioBackfillService.RepairSnapshotAsync"/> used to persist a snapshot
/// whenever at least ONE position could be priced (<c>priced == 0</c> guard). When only
/// some positions had a historical close, the written <c>MarketValue</c> was a partial sum
/// while <c>PositionCount</c> reflected ALL positions — producing days with a market value
/// far below their neighbours (e.g. 1,437 vs ~11.5M) and garbage daily-return spikes in the
/// calendar/trends. The fix requires EVERY reconstructed position to be priced
/// (<c>priced &lt; positions.Count</c>) before a snapshot is written/overwritten.
/// </para>
/// </summary>
public sealed class PortfolioBackfillServiceTests
{
    // A weekday with no existing snapshot. ReconstructPositions keeps positions whose latest
    // log entry is on-or-before this date with qty > 0, so logs are seeded earlier than this.
    private static readonly DateOnly TargetDate = new(2024, 1, 10); // Wednesday
    private static readonly DateOnly LogDate = new(2024, 1, 2);     // earlier weekday

    [Fact]
    public async Task BackfillAsync_PartialPricing_DoesNotWriteSnapshot()
    {
        // Two positions active on TargetDate, but history is available for only ONE of them.
        var logRepo = new FakeLogRepository(
            Log("2330", "TWSE", 10, 500m),
            Log("2317", "TWSE", 20, 100m));
        var snapshotRepo = new RecordingSnapshotRepository();
        var history = new FakeHistoryProvider();
        history.Add("2330", TargetDate, close: 600m); // 2317 deliberately omitted

        var service = new PortfolioBackfillService(logRepo, snapshotRepo, history);

        var written = await service.BackfillAsync();

        // The regression assertion: a partially-priced day must NOT be persisted.
        Assert.DoesNotContain(snapshotRepo.Writes, s => s.SnapshotDate == TargetDate);
        Assert.Equal(0, written);
    }

    [Fact]
    public async Task BackfillAsync_AllPositionsPriced_WritesSnapshotWithFullMarketValue()
    {
        var logRepo = new FakeLogRepository(
            Log("2330", "TWSE", 10, 500m),
            Log("2317", "TWSE", 20, 100m));
        var snapshotRepo = new RecordingSnapshotRepository();
        var history = new FakeHistoryProvider();
        history.Add("2330", TargetDate, close: 600m);
        history.Add("2317", TargetDate, close: 120m);

        var service = new PortfolioBackfillService(logRepo, snapshotRepo, history);

        var written = await service.BackfillAsync();

        var snapshot = Assert.Single(snapshotRepo.Writes, s => s.SnapshotDate == TargetDate);
        Assert.Equal(1, written);
        // MarketValue must be the FULL sum across every position, not a partial one.
        Assert.Equal((600m * 10) + (120m * 20), snapshot.MarketValue);
        Assert.Equal((500m * 10) + (100m * 20), snapshot.TotalCost);
        Assert.Equal(snapshot.MarketValue - snapshot.TotalCost, snapshot.Pnl);
        Assert.Equal(2, snapshot.PositionCount);
    }

    [Fact]
    public async Task BackfillAsync_SingleFullyPricedPosition_StillWrites()
    {
        // Existing behaviour must keep working: a day whose only position is priced is written.
        var logRepo = new FakeLogRepository(Log("2330", "TWSE", 10, 500m));
        var snapshotRepo = new RecordingSnapshotRepository();
        var history = new FakeHistoryProvider();
        history.Add("2330", TargetDate, close: 600m);

        var service = new PortfolioBackfillService(logRepo, snapshotRepo, history);

        var written = await service.BackfillAsync();

        var snapshot = Assert.Single(snapshotRepo.Writes, s => s.SnapshotDate == TargetDate);
        Assert.Equal(1, written);
        Assert.Equal(600m * 10, snapshot.MarketValue);
        Assert.Equal(1, snapshot.PositionCount);
    }

    [Fact]
    public async Task RepairSnapshotAsync_PartialPricing_ReturnsFalseAndDoesNotWrite()
    {
        var logRepo = new FakeLogRepository(
            Log("2330", "TWSE", 10, 500m),
            Log("2317", "TWSE", 20, 100m));
        var snapshotRepo = new RecordingSnapshotRepository();
        var history = new FakeHistoryProvider();
        history.Add("2330", TargetDate, close: 600m); // 2317 omitted -> partial

        var service = new PortfolioBackfillService(logRepo, snapshotRepo, history);

        var ok = await service.RepairSnapshotAsync(TargetDate);

        Assert.False(ok);
        Assert.Empty(snapshotRepo.Writes);
    }

    [Fact]
    public async Task RepairSnapshotAsync_AllPositionsPriced_ReturnsTrueWithFullMarketValue()
    {
        var logRepo = new FakeLogRepository(
            Log("2330", "TWSE", 10, 500m),
            Log("2317", "TWSE", 20, 100m));
        var snapshotRepo = new RecordingSnapshotRepository();
        var history = new FakeHistoryProvider();
        history.Add("2330", TargetDate, close: 600m);
        history.Add("2317", TargetDate, close: 120m);

        var service = new PortfolioBackfillService(logRepo, snapshotRepo, history);

        var ok = await service.RepairSnapshotAsync(TargetDate);

        Assert.True(ok);
        var snapshot = Assert.Single(snapshotRepo.Writes);
        Assert.Equal(TargetDate, snapshot.SnapshotDate);
        Assert.Equal((600m * 10) + (120m * 20), snapshot.MarketValue);
        Assert.Equal((500m * 10) + (100m * 20), snapshot.TotalCost);
        Assert.Equal(2, snapshot.PositionCount);
    }

    [Fact]
    public async Task BackfillAsync_SameDayMultiLotSell_ExcludesClosedPosition()
    {
        // WHY: a position sold in two trades on the SAME day leaves two log entries for one
        // PositionId: qty 20000 (first lot) then qty 0 (second lot). Reconstruction must take the
        // FINAL (0) → position closed → excluded. The old OrderByDescending(LogDate).First() is a
        // stable sort that does NOT tie-break same-day entries, so it picked the earlier 20000 →
        // a sold-out position was reconstructed as still held, inflating market value and producing
        // the calendar's phantom daily-return swing.
        var soldId = Guid.NewGuid();
        var logRepo = new FakeLogRepository(
            Log("2330", "TWSE", 10, 500m),               // held position (priced)
            Log(soldId, "3231", "TWSE", 20000, 100m),    // same day: after 1st lot → 20000 left
            Log(soldId, "3231", "TWSE", 0, 100m));       // same day: after 2nd lot → 0 (closed)
        var snapshotRepo = new RecordingSnapshotRepository();
        var history = new FakeHistoryProvider();
        history.Add("2330", TargetDate, close: 600m);
        history.Add("3231", TargetDate, close: 160m);    // priced too — old code would ADD 20000×160

        var service = new PortfolioBackfillService(logRepo, snapshotRepo, history);

        var written = await service.BackfillAsync();

        var snapshot = Assert.Single(snapshotRepo.Writes, s => s.SnapshotDate == TargetDate);
        Assert.Equal(1, written);
        // Sold-out 3231 excluded → only the held 2330 counts (old code: 2 positions, MV +3.2M).
        Assert.Equal(1, snapshot.PositionCount);
        Assert.Equal(600m * 10, snapshot.MarketValue);
    }

    private static PortfolioPositionLog Log(string symbol, string exchange, int qty, decimal buyPrice) =>
        new(Guid.NewGuid(), LogDate, Guid.NewGuid(), symbol, exchange, qty, buyPrice);

    private static PortfolioPositionLog Log(Guid positionId, string symbol, string exchange, int qty, decimal buyPrice) =>
        new(Guid.NewGuid(), LogDate, positionId, symbol, exchange, qty, buyPrice);

    private sealed class FakeLogRepository : IPortfolioPositionLogRepository
    {
        private readonly IReadOnlyList<PortfolioPositionLog> _logs;

        public FakeLogRepository(params PortfolioPositionLog[] logs) => _logs = logs;

        public Task<IReadOnlyList<PortfolioPositionLog>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult(_logs);

        public Task UpdateLogDateAsync(Guid logId, DateOnly newDate, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<bool> HasAnyAsync(CancellationToken ct = default) =>
            Task.FromResult(_logs.Count > 0);

        public Task LogAsync(PortfolioPositionLog entry, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task LogBatchAsync(IEnumerable<PortfolioPositionLog> entries, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingSnapshotRepository : IPortfolioSnapshotRepository
    {
        public List<PortfolioDailySnapshot> Writes { get; } = [];

        public Task<IReadOnlyList<PortfolioDailySnapshot>> GetSnapshotsAsync(
            DateOnly? from = null,
            DateOnly? to = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PortfolioDailySnapshot>>(Writes);

        public Task<PortfolioDailySnapshot?> GetSnapshotAsync(DateOnly date, CancellationToken ct = default) =>
            Task.FromResult(Writes.LastOrDefault(s => s.SnapshotDate == date));

        public Task UpsertAsync(PortfolioDailySnapshot snapshot, CancellationToken ct = default)
        {
            Writes.Add(snapshot);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHistoryProvider : IStockHistoryProvider
    {
        // symbol -> (date -> close)
        private readonly Dictionary<string, Dictionary<DateOnly, decimal>> _bySymbol = new();

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
}
