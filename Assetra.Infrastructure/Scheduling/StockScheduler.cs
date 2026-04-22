using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.Http;

namespace Assetra.Infrastructure.Scheduling;

internal sealed class StockScheduler : IStockService
{
    private readonly ITwseClient _twse;
    private readonly ITpexClient _tpex;
    private readonly IPortfolioRepository _portfolio;
    private readonly IAppSettingsService _settings;
    private readonly FugleClient _fugle;
    private readonly IScheduler _scheduler;
    private readonly TimeSpan _interval;
    // ReplaySubject(1) replays the last emission to any late subscriber,
    // preventing missed initial values.
    private readonly ReplaySubject<IReadOnlyList<StockQuote>> _subject = new(1);
    private readonly CompositeDisposable _disposables = new();

    public IObservable<IReadOnlyList<StockQuote>> QuoteStream => _subject.AsObservable();

    public StockScheduler(ITwseClient twse, ITpexClient tpex,
        IPortfolioRepository portfolio,
        IAppSettingsService settings,
        FugleClient fugle,
        IScheduler scheduler, TimeSpan? interval = null)
    {
        _twse = twse;
        _tpex = tpex;
        _portfolio = portfolio;
        _settings = settings;
        _fugle = fugle;
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
        var portfolioEntries = await _portfolio.GetEntriesAsync();

        var entries = portfolioEntries
            .Select(e => (e.Symbol, e.Exchange))
            .DistinctBy(e => e.Symbol)
            .ToList();

        var twseSymbols = entries.Where(e => e.Exchange == "TWSE").Select(e => e.Symbol).ToList();
        var tpexSymbols = entries.Where(e => e.Exchange == "TPEX").Select(e => e.Symbol).ToList();
        var useFugle = string.Equals(_settings.Current.QuoteProvider, "fugle", StringComparison.OrdinalIgnoreCase)
                       && _fugle.IsConfigured;

        if (useFugle)
        {
            var fugleResults = await Task.WhenAll(entries.Select(async e =>
            {
                var quote = await _fugle.FetchQuoteAsync(e.Symbol).ConfigureAwait(false);
                return quote;
            })).ConfigureAwait(false);

            var available = fugleResults.Where(q => q is not null).Cast<StockQuote>().ToList();
            var missingSymbols = entries
                .Where(e => available.All(q => q.Symbol != e.Symbol))
                .ToList();

            if (missingSymbols.Count == 0)
                return available;

            twseSymbols = missingSymbols.Where(e => e.Exchange == "TWSE").Select(e => e.Symbol).ToList();
            tpexSymbols = missingSymbols.Where(e => e.Exchange == "TPEX").Select(e => e.Symbol).ToList();
            var fallback = await FetchOfficialAsync(twseSymbols, tpexSymbols).ConfigureAwait(false);
            return [.. available, .. fallback];
        }

        return await FetchOfficialAsync(twseSymbols, tpexSymbols).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<StockQuote>> FetchOfficialAsync(
        IReadOnlyList<string> twseSymbols,
        IReadOnlyList<string> tpexSymbols)
    {
        var twseTask = twseSymbols.Count > 0
            ? _twse.FetchQuotesAsync(twseSymbols)
            : Task.FromResult<IReadOnlyList<StockQuote>>([]);
        var tpexTask = tpexSymbols.Count > 0
            ? _tpex.FetchQuotesAsync(tpexSymbols)
            : Task.FromResult<IReadOnlyList<StockQuote>>([]);

        var twseResults = await twseTask.ConfigureAwait(false);
        var tpexResults = await tpexTask.ConfigureAwait(false);
        return [.. twseResults, .. tpexResults];
    }

    public void Dispose()
    {
        _disposables.Dispose();
        _subject.Dispose();
    }
}
