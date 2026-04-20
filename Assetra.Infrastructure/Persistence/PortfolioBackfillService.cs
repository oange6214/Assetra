using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// Fills gaps in <c>portfolio_daily_snapshot</c> by reconstructing missing trading-day
/// values from the position change log combined with historical closing prices.
///
/// Algorithm:
///   1. Find weekdays with no snapshot between the earliest log entry and yesterday.
///   2. For each missing date, find active positions (latest log entry ≤ date, qty > 0).
///   3. Look up the closing price for that date from price history.
///   4. Write a backfilled snapshot via <see cref="IPortfolioSnapshotRepository.UpsertAsync"/>.
///
/// Runs fire-and-forget; failures are silently swallowed so they cannot affect the main flow.
/// </summary>
public sealed class PortfolioBackfillService
{
    private readonly IPortfolioPositionLogRepository _logRepo;
    private readonly IPortfolioSnapshotRepository _snapshotRepo;
    private readonly IStockHistoryProvider _historyProvider;
    private readonly ILogger<PortfolioBackfillService> _logger;

    public PortfolioBackfillService(
        IPortfolioPositionLogRepository logRepo,
        IPortfolioSnapshotRepository snapshotRepo,
        IStockHistoryProvider historyProvider,
        ILogger<PortfolioBackfillService>? logger = null)
    {
        _logRepo = logRepo;
        _snapshotRepo = snapshotRepo;
        _historyProvider = historyProvider;
        _logger = logger ?? NullLogger<PortfolioBackfillService>.Instance;
    }

    /// <summary>
    /// Detects and fills snapshot gaps.  Returns the number of snapshots written.
    /// </summary>
    public async Task<int> BackfillAsync(CancellationToken ct = default)
    {
        var logs = await _logRepo.GetAllAsync();
        if (logs.Count == 0)
            return 0;

        // Dates we already have
        var existing = (await _snapshotRepo.GetSnapshotsAsync())
            .Select(s => s.SnapshotDate)
            .ToHashSet();

        var earliest = logs.Min(l => l.LogDate);
        var yesterday = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));

        if (earliest > yesterday)
            return 0;

        var missingDates = Weekdays(earliest, yesterday)
            .Where(d => !existing.Contains(d))
            .ToList();

        if (missingDates.Count == 0)
            return 0;

        // Fetch price history once per unique symbol (up to TwoYears)
        var symbols = logs
            .Select(l => (l.Symbol, l.Exchange))
            .Distinct()
            .ToList();

        var prices = new Dictionary<string, Dictionary<DateOnly, decimal>>();
        foreach (var (symbol, exchange) in symbols)
        {
            if (ct.IsCancellationRequested)
                break;
            try
            {
                var history = await _historyProvider.GetHistoryAsync(
                    symbol, exchange, ChartPeriod.TwoYears, ct);
                prices[symbol] = history.ToDictionary(h => h.Date, h => h.Close);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Backfill: could not fetch price history for {Symbol}", symbol);
            }
        }

        int written = 0;
        foreach (var date in missingDates)
        {
            if (ct.IsCancellationRequested)
                break;

            var positions = ReconstructPositions(logs, date);
            if (positions.Count == 0)
                continue;

            decimal totalCost = 0m, totalMarketValue = 0m;
            int priced = 0;

            foreach (var pos in positions)
            {
                if (!prices.TryGetValue(pos.Symbol, out var byDate))
                    continue;
                if (!byDate.TryGetValue(date, out var close))
                    continue;

                totalCost += pos.BuyPrice * pos.Quantity;
                totalMarketValue += close * pos.Quantity;
                priced++;
            }

            // Only write if we got prices for at least one position
            if (priced == 0)
                continue;

            var snapshot = new PortfolioDailySnapshot(
                date,
                totalCost,
                totalMarketValue,
                totalMarketValue - totalCost,
                positions.Count);

            try
            {
                await _snapshotRepo.UpsertAsync(snapshot).ConfigureAwait(false);
                written++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Backfill: failed to write snapshot for {Date}", date);
            }
        }

        return written;
    }

    // helpers

    /// <summary>
    /// Reconstructs the active positions on <paramref name="date"/> by taking,
    /// for each position id, the latest log entry whose date is ≤ <paramref name="date"/>.
    /// Entries with Quantity == 0 represent closed positions and are excluded.
    /// </summary>
    private static IReadOnlyList<PortfolioPositionLog> ReconstructPositions(
        IReadOnlyList<PortfolioPositionLog> allLogs, DateOnly date) =>
        allLogs
            .Where(l => l.LogDate <= date)
            .GroupBy(l => l.PositionId)
            .Select(g => g.OrderByDescending(l => l.LogDate).First())
            .Where(l => l.Quantity > 0)
            .ToList();

    /// <summary>Enumerates every Monday–Friday between <paramref name="from"/> and <paramref name="to"/> inclusive.</summary>
    private static IEnumerable<DateOnly> Weekdays(DateOnly from, DateOnly to)
    {
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;
            yield return d;
        }
    }
}
