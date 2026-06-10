using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// Reconstructs FULL historical <see cref="PortfolioDailySnapshot"/> rows (market value +
/// cash / equity / liability breakdown) for a past date range, matching the LIVE snapshot 口徑.
///
/// <para>
/// Unlike <see cref="PortfolioBackfillService"/> (which only fills market value and was never
/// multi-currency / breakdown aware), this service reproduces the same numbers the live
/// <c>PortfolioViewModel</c> writes today:
/// </para>
/// <list type="bullet">
///   <item>Equity = Σ historical-close × qty, each position converted to base currency via
///         as-of-D FX (<see cref="IMultiCurrencyValuationService.ConvertAsync"/>). The position's
///         currency is resolved from its <see cref="PortfolioEntry.Currency"/> — the exact source
///         the live path uses (<c>row.Currency = entry.Currency</c>).</item>
///   <item>Cash = Σ <see cref="IBalanceQueryService.GetAllCashBalancesAsOfAsync"/>, each converted to base via as-of-D FX.</item>
///   <item>Liability = Σ <see cref="IBalanceQueryService.GetAllLiabilitySnapshotsAsOfAsync"/>, converted the same way.</item>
/// </list>
///
/// <para>
/// <b>All-or-nothing per day</b> (mirrors the Q09 discipline in <see cref="PortfolioBackfillService"/>):
/// if ANY position lacks a historical close, or ANY amount cannot be FX-converted for that date,
/// the whole day is skipped — never a partial sum. A live row (existing snapshot already carrying a
/// breakdown) is preserved untouched.
/// </para>
///
/// <para>Stage 1 scope: pure orchestration over injected repos/providers; no UI / command wiring.</para>
/// </summary>
public sealed class PortfolioSnapshotRebuildService
{
    private readonly IPortfolioPositionLogRepository _logRepo;
    private readonly IPortfolioSnapshotRepository _snapshotRepo;
    private readonly IStockHistoryProvider _historyProvider;
    private readonly IMultiCurrencyValuationService _fx;
    private readonly IBalanceQueryService _balances;
    private readonly IPortfolioRepository _portfolio;
    private readonly IAppSettingsService? _settings;
    private readonly ILogger<PortfolioSnapshotRebuildService> _logger;

    public PortfolioSnapshotRebuildService(
        IPortfolioPositionLogRepository logRepo,
        IPortfolioSnapshotRepository snapshotRepo,
        IStockHistoryProvider historyProvider,
        IMultiCurrencyValuationService fx,
        IBalanceQueryService balances,
        IPortfolioRepository portfolio,
        IAppSettingsService? settings = null,
        ILogger<PortfolioSnapshotRebuildService>? logger = null)
    {
        _logRepo = logRepo ?? throw new ArgumentNullException(nameof(logRepo));
        _snapshotRepo = snapshotRepo ?? throw new ArgumentNullException(nameof(snapshotRepo));
        _historyProvider = historyProvider ?? throw new ArgumentNullException(nameof(historyProvider));
        _fx = fx ?? throw new ArgumentNullException(nameof(fx));
        _balances = balances ?? throw new ArgumentNullException(nameof(balances));
        _portfolio = portfolio ?? throw new ArgumentNullException(nameof(portfolio));
        _settings = settings;
        _logger = logger ?? NullLogger<PortfolioSnapshotRebuildService>.Instance;
    }

    /// <summary>
    /// Rebuilds full-breakdown snapshots for every weekday in <c>[from, to]</c> inclusive.
    /// </summary>
    /// <param name="from">Range start (inclusive).</param>
    /// <param name="to">Range end (inclusive).</param>
    /// <param name="dryRun">
    /// When <see langword="true"/>, computes and reports every day's would-be values but writes nothing.
    /// </param>
    public async Task<SnapshotRebuildReport> RebuildAsync(
        DateOnly from, DateOnly to, bool dryRun, CancellationToken ct = default)
    {
        var days = new List<SnapshotRebuildDayResult>();
        if (from > to)
            return new SnapshotRebuildReport(from, to, dryRun, days);

        var baseCcy = ResolveBaseCurrency();
        var logs = await _logRepo.GetAllAsync(ct).ConfigureAwait(false);

        // Existing snapshots read ONCE up front — used to preserve live (breakdown-bearing) rows.
        var existing = (await _snapshotRepo.GetSnapshotsAsync(from, to, ct).ConfigureAwait(false))
            .GroupBy(s => s.SnapshotDate)
            .ToDictionary(g => g.Key, g => g.Last());

        // Currency per (symbol, exchange) — resolved from PortfolioEntry, the live path's source.
        var currencyResolver = CurrencyResolver.Build(
            await _portfolio.GetEntriesAsync(ct).ConfigureAwait(false), baseCcy);

        // Fetch each symbol's price history once (cache symbol -> date -> close), like BackfillService.
        var priceCache = await BuildPriceCacheAsync(logs, ct).ConfigureAwait(false);

        foreach (var date in Weekdays(from, to))
        {
            ct.ThrowIfCancellationRequested();
            var result = await RebuildDayAsync(
                date, logs, existing, currencyResolver, priceCache, baseCcy, dryRun, ct)
                .ConfigureAwait(false);
            days.Add(result);
        }

        return new SnapshotRebuildReport(from, to, dryRun, days);
    }

    private async Task<SnapshotRebuildDayResult> RebuildDayAsync(
        DateOnly date,
        IReadOnlyList<PortfolioPositionLog> logs,
        IReadOnlyDictionary<DateOnly, PortfolioDailySnapshot> existing,
        CurrencyResolver currencyResolver,
        IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, decimal>> priceCache,
        string baseCcy,
        bool dryRun,
        CancellationToken ct)
    {
        existing.TryGetValue(date, out var existingRow);
        var oldMarketValue = existingRow?.MarketValue;

        // 1. Preserve live rows: any snapshot already carrying a breakdown component is left untouched.
        if (HasBreakdown(existingRow))
            return Skip(date, oldMarketValue, RebuildDayStatus.SkippedHasCompleteLiveRow);

        // 2. Reconstruct as-of-D positions.
        var positions = ReconstructPositions(logs, date);
        if (positions.Count == 0)
            return Skip(date, oldMarketValue, RebuildDayStatus.SkippedNoPositions);

        // 3. Equity (all-or-nothing, FX-converted). Cost converted the same way as the live path.
        decimal equity = 0m, cost = 0m;
        foreach (var pos in positions)
        {
            if (!priceCache.TryGetValue(pos.Symbol, out var byDate) ||
                !byDate.TryGetValue(date, out var close))
            {
                return Skip(date, oldMarketValue, RebuildDayStatus.SkippedUnpriceable);
            }

            var ccy = currencyResolver.Resolve(pos.Symbol, pos.Exchange);
            var convertedMv = await _fx.ConvertAsync(close * pos.Quantity, ccy, baseCcy, date, ct).ConfigureAwait(false);
            var convertedCost = await _fx.ConvertAsync(pos.BuyPrice * pos.Quantity, ccy, baseCcy, date, ct).ConfigureAwait(false);
            if (convertedMv is null || convertedCost is null)
                return Skip(date, oldMarketValue, RebuildDayStatus.SkippedNoFx);

            equity += convertedMv.Value;
            cost += convertedCost.Value;
        }

        // 4. Cash (as-of-D), each account FX-converted to base.
        var cash = await SumConvertedAsOfAsync(
            (await _balances.GetAllCashBalancesAsOfAsync(date, ct).ConfigureAwait(false)).Values,
            baseCcy, date, ct).ConfigureAwait(false);
        if (cash is null)
            return Skip(date, oldMarketValue, RebuildDayStatus.SkippedNoFx);

        // 5. Liability (as-of-D), each loan FX-converted to base.
        var liability = await SumConvertedAsOfAsync(
            (await _balances.GetAllLiabilitySnapshotsAsOfAsync(date, ct).ConfigureAwait(false))
                .Values.Select(s => s.Balance),
            baseCcy, date, ct).ConfigureAwait(false);
        if (liability is null)
            return Skip(date, oldMarketValue, RebuildDayStatus.SkippedNoFx);

        // 6. Build the full-breakdown snapshot. MarketValue == EquityValue (mirrors live: the live
        //    path passes equityValue defaulting to marketValue, both = FX-converted equity).
        var snapshot = new PortfolioDailySnapshot(
            date,
            cost,
            MarketValue: equity,
            Pnl: equity - cost,
            positions.Count,
            baseCcy,
            CashValue: cash.Value,
            EquityValue: equity,
            LiabilityValue: liability.Value);

        // 7. Persist unless dry-run.
        if (!dryRun)
            await _snapshotRepo.UpsertAsync(snapshot, ct).ConfigureAwait(false);

        return new SnapshotRebuildDayResult(
            date, oldMarketValue, equity, cash.Value, equity, liability.Value, RebuildDayStatus.Rebuilt);
    }

    /// <summary>
    /// Converts each <see cref="Money"/> to <paramref name="baseCcy"/> as of <paramref name="date"/>
    /// and sums. Returns <see langword="null"/> the instant any conversion lacks a rate
    /// (all-or-nothing — same discipline as the equity loop).
    /// </summary>
    private async Task<decimal?> SumConvertedAsOfAsync(
        IEnumerable<Money> amounts, string baseCcy, DateOnly date, CancellationToken ct)
    {
        decimal sum = 0m;
        foreach (var money in amounts)
        {
            var converted = await _fx.ConvertAsync(money.Amount, money.Currency, baseCcy, date, ct).ConfigureAwait(false);
            if (converted is null)
                return null;
            sum += converted.Value;
        }
        return sum;
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<DateOnly, decimal>>> BuildPriceCacheAsync(
        IReadOnlyList<PortfolioPositionLog> logs, CancellationToken ct)
    {
        var cache = new Dictionary<string, IReadOnlyDictionary<DateOnly, decimal>>(StringComparer.Ordinal);
        var symbols = logs.Select(l => (l.Symbol, l.Exchange)).Distinct().ToList();
        foreach (var (symbol, exchange) in symbols)
        {
            ct.ThrowIfCancellationRequested();
            if (cache.ContainsKey(symbol))
                continue;
            try
            {
                var history = await _historyProvider
                    .GetHistoryAsync(symbol, exchange, ChartPeriod.TwoYears, ct)
                    .ConfigureAwait(false);
                // Last-write-wins on duplicate dates keeps this resilient to provider quirks.
                var byDate = new Dictionary<DateOnly, decimal>();
                foreach (var h in history)
                    byDate[h.Date] = h.Close;
                cache[symbol] = byDate;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "Rebuild: could not fetch price history for {Symbol}", symbol);
            }
        }
        return cache;
    }

    private string ResolveBaseCurrency()
    {
        var baseCcy = _settings?.Current?.BaseCurrency;
        return string.IsNullOrWhiteSpace(baseCcy) ? IBalanceQueryService.DefaultCurrency : baseCcy;
    }

    private static bool HasBreakdown(PortfolioDailySnapshot? snapshot) =>
        snapshot is not null &&
        (snapshot.CashValue is not null || snapshot.EquityValue is not null || snapshot.LiabilityValue is not null);

    private static SnapshotRebuildDayResult Skip(
        DateOnly date, decimal? oldMarketValue, RebuildDayStatus status) =>
        new(date, oldMarketValue, null, null, null, null, status);

    /// <summary>
    /// Reconstructs active positions on <paramref name="date"/> — identical rule to
    /// <see cref="PortfolioBackfillService"/>: latest log entry per position with date ≤ date and qty &gt; 0.
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

    /// <summary>
    /// Maps a position's (symbol, exchange) to its valuation currency, mirroring the live path
    /// where each row's currency comes from <see cref="PortfolioEntry.Currency"/>. Exact
    /// (symbol+exchange) match wins; falls back to a symbol-only match for legacy entries with a
    /// blank exchange (same leniency as the live quote matcher); failing both, the base currency.
    /// </summary>
    private sealed class CurrencyResolver
    {
        private readonly IReadOnlyDictionary<(string Symbol, string Exchange), string> _exact;
        private readonly IReadOnlyDictionary<string, string> _bySymbol;
        private readonly string _fallback;

        private CurrencyResolver(
            IReadOnlyDictionary<(string, string), string> exact,
            IReadOnlyDictionary<string, string> bySymbol,
            string fallback)
        {
            _exact = exact;
            _bySymbol = bySymbol;
            _fallback = fallback;
        }

        public static CurrencyResolver Build(IReadOnlyList<PortfolioEntry> entries, string fallback)
        {
            var exact = new Dictionary<(string, string), string>();
            var bySymbol = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                if (string.IsNullOrWhiteSpace(e.Symbol) || string.IsNullOrWhiteSpace(e.Currency))
                    continue;
                exact[(Norm(e.Symbol), Norm(e.Exchange))] = e.Currency;
                // Symbol-only fallback: last entry wins; only consulted when no exact match exists.
                bySymbol[e.Symbol] = e.Currency;
            }
            return new CurrencyResolver(exact, bySymbol, fallback);
        }

        public string Resolve(string symbol, string exchange)
        {
            if (_exact.TryGetValue((Norm(symbol), Norm(exchange)), out var ccy))
                return ccy;
            if (_bySymbol.TryGetValue(symbol ?? string.Empty, out var bySym))
                return bySym;
            return _fallback;
        }

        private static string Norm(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();
    }
}
