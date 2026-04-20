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
    private readonly IScheduler _scheduler;
    private readonly TimeSpan _interval;
    // ReplaySubject(1) replays the last emission to any late subscriber,
    // preventing missed initial values.
    private readonly ReplaySubject<IReadOnlyList<StockQuote>> _subject = new(1);
    private readonly CompositeDisposable _disposables = new();

    public IObservable<IReadOnlyList<StockQuote>> QuoteStream => _subject.AsObservable();

    public StockScheduler(ITwseClient twse, ITpexClient tpex,
        IPortfolioRepository portfolio,
        IScheduler scheduler, TimeSpan? interval = null)
    {
        _twse = twse;
        _tpex = tpex;
        _portfolio = portfolio;
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

        var twseTask = twseSymbols.Count > 0
            ? _twse.FetchQuotesAsync(twseSymbols)
            : Task.FromResult<IReadOnlyList<StockQuote>>([]);
        var tpexTask = tpexSymbols.Count > 0
            ? _tpex.FetchQuotesAsync(tpexSymbols)
            : Task.FromResult<IReadOnlyList<StockQuote>>([]);

        var twseResults = await twseTask;
        var tpexResults = await tpexTask;
        return [.. twseResults, .. tpexResults];
    }

    public void Dispose()
    {
        _disposables.Dispose();
        _subject.Dispose();
    }
}
