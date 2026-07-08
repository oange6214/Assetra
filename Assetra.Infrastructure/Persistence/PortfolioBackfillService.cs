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
///   3. Look up the closing price for that date (falling back to the most recent close within a
///      small window when the exact-day close hasn't arrived yet — forward-fill) from price history.
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

    /// <summary>
    /// Forward-fill 窗：某檔在目標日缺收盤時，往前找最近一筆收盤的最大天數。足以涵蓋週末＋一般假日
    /// （含農曆年連假）＋資料來源晚到幾天；超過此窗（太久沒價）就視為無價 → 該天留白，不用過期的價。
    /// </summary>
    private const int ForwardFillWindowDays = 14;

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
                // Forward-fill：某檔在 date 當天沒收盤（來源尚未出、或當天休市/假日）時，用「date 之前
                // 最近一筆、且不超過 ForwardFillWindowDays 天」的收盤價往前補。這樣不同市場（台股/美股）
                // 交易日／資料新舊不一致時，不會因為一檔晚到就整天留白（使用者實例：00988A 收盤只到
                // 07-03、DRAM 到 07-07 → 07-06/07 原本被跳過）。窗外（太久沒價）才視為無價。
                if (PriceAsOf(byDate, date, ForwardFillWindowDays) is not { } close)
                    continue;

                totalCost += pos.BuyPrice * pos.Quantity;
                totalMarketValue += close * pos.Quantity;
                priced++;
            }

            // Only write a snapshot when EVERY reconstructed position was priced (含 forward-fill）。
            // 仍保留「全有或全無」：某檔連 window 內都沒有任何收盤（全新標的/長期無資料）→ 該天留白，
            // 不寫入部分口徑。partial sum 會低估市值、在日曆/走勢做出假的當日報酬暴跌（如某天 market_value≈0
            // 而鄰日 ~11.5M）——forward-fill 用的是真實近日收盤（非 0），故不會造成那種 garbage。
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
                _logger.LogWarning(ex, "Repair: could not fetch price history for {Symbol}", symbol);
            }
        }

        decimal totalCost = 0m, totalMarketValue = 0m;
        int priced = 0;
        foreach (var pos in positions)
        {
            // 與 BackfillAsync 同：當天缺價則用 window 內最近一筆收盤 forward-fill。
            if (!prices.TryGetValue(pos.Symbol, out var byDate)
                || PriceAsOf(byDate, date, ForwardFillWindowDays) is not { } close)
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

    /// <summary>
    /// 取 <paramref name="date"/> 當天收盤；缺當天時，回傳「date 之前、且在 <paramref name="maxStaleDays"/>
    /// 天內」最近一筆收盤（forward-fill）。窗內完全沒有任何收盤 → null（視為無價）。
    /// </summary>
    private static decimal? PriceAsOf(
        IReadOnlyDictionary<DateOnly, decimal> byDate, DateOnly date, int maxStaleDays)
    {
        if (byDate.TryGetValue(date, out var exact))
            return exact;

        var floor = date.AddDays(-maxStaleDays);
        DateOnly? best = null;
        foreach (var d in byDate.Keys)
            if (d < date && d >= floor && (best is null || d > best.Value))
                best = d;
        return best is { } b ? byDate[b] : null;
    }

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
