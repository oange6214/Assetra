using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Scheduling;

internal sealed class StockScheduler : IStockService
{
    private readonly IEquityRouter _router;
    private readonly IPortfolioRepository _portfolio;
    private readonly IAlertRepository _alerts;
    private readonly ITradingCalendarService _calendar;
    private readonly TimeProvider _timeProvider;
    private readonly IScheduler _scheduler;
    private readonly TimeSpan _interval;
    // ReplaySubject(1) replays the last emission to any late subscriber,
    // preventing missed initial values.
    private readonly ReplaySubject<IReadOnlyList<StockQuote>> _subject = new(1);
    private readonly CompositeDisposable _disposables = new();

    /// <summary>
    /// Tracks which (symbol, exchange) pairs have already received at least one
    /// quote tick. Off-hours US fetches (which <see cref="ShouldFetchQuotes"/>
    /// would normally suppress) are still allowed for cold rows so the user can
    /// see last-close prices immediately after adding a position outside market
    /// hours. Subsequent polls respect the calendar gate so quota isn't burned
    /// refreshing static last-close data every interval.
    /// </summary>
    private readonly HashSet<(string Symbol, string Exchange)> _seenEntries =
        new(SeenEntryComparer.Instance);

    private sealed class SeenEntryComparer : IEqualityComparer<(string Symbol, string Exchange)>
    {
        public static readonly SeenEntryComparer Instance = new();

        public bool Equals((string Symbol, string Exchange) x, (string Symbol, string Exchange) y) =>
            string.Equals(x.Symbol, y.Symbol, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Exchange, y.Exchange, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Symbol, string Exchange) obj) => HashCode.Combine(
            obj.Symbol.ToUpperInvariant(),
            obj.Exchange.ToUpperInvariant());
    }

    public IObservable<IReadOnlyList<StockQuote>> QuoteStream => _subject.AsObservable();

    public StockScheduler(
        IEquityRouter router,
        IPortfolioRepository portfolio,
        IAlertRepository alerts,
        IScheduler scheduler,
        TimeSpan? interval = null,
        ITradingCalendarService? calendar = null,
        TimeProvider? timeProvider = null)
    {
        _router = router;
        _portfolio = portfolio;
        _alerts = alerts;
        _calendar = calendar ?? AlwaysOpenTradingCalendar.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _scheduler = scheduler;
        _interval = interval ?? TimeSpan.FromSeconds(10);
    }

    public void Start()
    {
        if (_disposables.Count > 0)
            return;

        var subscription = Observable
            .Timer(TimeSpan.Zero, _interval, _scheduler)
            .SelectMany(_ =>
                Observable.FromAsync(FetchAllAsync)
                          .Catch((Exception _) => Observable.Empty<IReadOnlyList<StockQuote>>()))
            .Subscribe(v => _subject.OnNext(v));

        _disposables.Add(subscription);
    }

    public void Stop() => _disposables.Clear();

    private async Task<IReadOnlyList<StockQuote>> FetchAllAsync()
    {
        var portfolioEntries = await _portfolio.GetEntriesAsync().ConfigureAwait(false);
        var alertRules = await _alerts.GetRulesAsync().ConfigureAwait(false);

        var entries = portfolioEntries
            .Select(e => (e.Symbol, e.Exchange))
            .Concat(alertRules
                .Where(r => !r.IsTriggered)
                .Select(r => (r.Symbol, r.Exchange)))
            .Select(e => (Symbol: NormalizeSymbol(e.Symbol), Exchange: NormalizeExchange(e.Exchange)))
            .Where(e => e.Symbol.Length > 0 && e.Exchange.Length > 0)
            .DistinctBy(e => (e.Symbol, e.Exchange))
            .ToList();

        var now = _timeProvider.GetUtcNow();

        // Two-gate filter:
        //   1. Market-hours filter: only entries whose exchange is in session right now
        //   2. Cold-row bypass: entries we've never quoted at least once are ALWAYS allowed
        //      through, regardless of session, so the user sees last-close immediately
        //      after adding a foreign position outside US trading hours.
        entries = entries
            .Where(e => ShouldFetchQuotes(e.Exchange, now) || !_seenEntries.Contains((e.Symbol, e.Exchange)))
            .ToList();

        if (entries.Count == 0)
            return [];

        var quoteResults = await _router.GetQuotesAsync(
            entries.Select(e => new EquityInstrumentKey(e.Symbol, e.Exchange)).ToList(),
            EquityQuoteCachePolicies.SchedulerRefresh)
            .ConfigureAwait(false);

        var quotes = quoteResults
            .Where(r => r.IsSuccess && r.Value is not null)
            .Select(r => EquityQuoteLegacyMapper.ToStockQuote(r.Value!))
            .ToList();

        // Mark every entry we actually attempted as seen (success or fail).
        // Failed cold rows shouldn't keep re-firing every 10s — quota protection.
        // If the underlying issue resolves (e.g. user adds API key), restart picks
        // them up via the regular hours gate or app relaunch resets _seenEntries.
        foreach (var entry in entries)
            _seenEntries.Add((entry.Symbol, entry.Exchange));

        return quotes;
    }

    private static string NormalizeSymbol(string symbol) =>
        symbol.Trim().ToUpperInvariant();

    private static string NormalizeExchange(string exchange) =>
        exchange.Trim().ToUpperInvariant();

    private bool ShouldFetchQuotes(string exchange, DateTimeOffset now) =>
        IsTaiwanExchange(exchange) || _calendar.ShouldRefreshQuotes(exchange, now);

    private static bool IsTaiwanExchange(string exchange) =>
        string.Equals(exchange, "TWSE", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(exchange, "TPEX", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        _disposables.Dispose();
        _subject.Dispose();
    }

    private sealed class AlwaysOpenTradingCalendar : ITradingCalendarService
    {
        public static readonly AlwaysOpenTradingCalendar Instance = new();

        private AlwaysOpenTradingCalendar()
        {
        }

        public TradingDayKind GetTradingDayKind(string exchange, DateOnly localDate) => TradingDayKind.FullSession;

        public bool ShouldRefreshQuotes(string exchange, DateTimeOffset utcNow) => true;
    }
}
