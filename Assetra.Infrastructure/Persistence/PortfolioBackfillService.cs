using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
        var logs = await _logRepo.GetAllAsync(ct).ConfigureAwait(false);
        if (logs.Count == 0)
            return 0;

        // Dates we already have
        var existing = (await _snapshotRepo.GetSnapshotsAsync(ct: ct).ConfigureAwait(false))
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
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
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

            // Only write a snapshot when EVERY reconstructed position was priced.
            // A partial sum (some positions unpriced) understates total market value and
            // produces garbage daily-return deltas in the calendar/trends (e.g. a day that
            // recorded market_value≈0 while neighbours are ~11.5M). Better to leave the day
            // blank than to persist a partial-口徑 value.
            if (priced < positions.Count)
                continue;

            var snapshot = new PortfolioDailySnapshot(
                date,
                totalCost,
                totalMarketValue,
                totalMarketValue - totalCost,
                positions.Count);

            try
            {
                await _snapshotRepo.UpsertAsync(snapshot, ct).ConfigureAwait(false);
                written++;
            }
            catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Backfill: failed to write snapshot for {Date}", date);
            }
        }

        return written;
    }

    /// <summary>
    /// 強制用 history price 重建單一指定日的 snapshot，覆蓋既有資料。
    /// <para>
    /// 用途：使用者察覺某日 trend chart 跳水 / 跳高，懷疑當天是 partial-price
    /// snapshot 假象（如 app 啟動時某檔 quote 還沒回來）。按下「修復這一日」就會
    /// 用歷史收盤價重算，UPSERT 覆蓋。
    /// </para>
    /// <para>
    /// 回傳 true 代表成功覆寫；false 代表沒有對應 position log / 抓不到歷史價 /
    /// 寫入失敗。
    /// </para>
    /// </summary>
    public async Task<bool> RepairSnapshotAsync(DateOnly date, CancellationToken ct = default)
    {
        var logs = await _logRepo.GetAllAsync(ct).ConfigureAwait(false);
        if (logs.Count == 0)
            return false;

        var positions = ReconstructPositions(logs, date);
        if (positions.Count == 0)
            return false;

        var symbols = positions.Select(p => (p.Symbol, p.Exchange)).Distinct().ToList();
        var prices = new Dictionary<string, decimal>();
        foreach (var (symbol, exchange) in symbols)
        {
            if (ct.IsCancellationRequested)
                break;
            try
            {
                var history = await _historyProvider.GetHistoryAsync(
                    symbol, exchange, ChartPeriod.TwoYears, ct);
                var match = history.FirstOrDefault(h => h.Date == date);
                if (match != default)
                    prices[symbol] = match.Close;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Repair: could not fetch price history for {Symbol}", symbol);
            }
        }

        decimal totalCost = 0m, totalMarketValue = 0m;
        int priced = 0;
        foreach (var pos in positions)
        {
            if (!prices.TryGetValue(pos.Symbol, out var close))
                continue;
            totalCost += pos.BuyPrice * pos.Quantity;
            totalMarketValue += close * pos.Quantity;
            priced++;
        }

        // Repair must be all-or-nothing: never overwrite with a partial-priced value
        // (that is exactly the corruption this repair is meant to undo).
        if (priced < positions.Count)
            return false;

        var snapshot = new PortfolioDailySnapshot(
            date,
            totalCost,
            totalMarketValue,
            totalMarketValue - totalCost,
            positions.Count);

        try
        {
            await _snapshotRepo.UpsertAsync(snapshot, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Repair: failed to write snapshot for {Date}", date);
            return false;
        }
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
            // allLogs 由 repository 依 (log_date, rowid) 升冪排序；GroupBy 保留群組內來源順序，
            // 故 g.Last() = 該部位「日期 ≤ date 的最後一筆」＝最終狀態。
            // 修正：同日多筆（如同日分批賣出 20000→0）舊版 OrderByDescending(LogDate).First() 是穩定
            // 排序、同日不分先後，會誤取較早一筆（20000）→ 已平倉部位被當成仍持有。
            .Where(l => l.LogDate <= date)
            .GroupBy(l => l.PositionId)
            .Select(g => g.Last())
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
