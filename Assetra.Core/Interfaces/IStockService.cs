using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IStockService : IDisposable
{
    IObservable<IReadOnlyList<StockQuote>> QuoteStream { get; }
    void Start();
    void Stop();
}
