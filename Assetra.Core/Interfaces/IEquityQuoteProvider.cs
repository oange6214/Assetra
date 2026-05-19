using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IEquityQuoteProvider
{
    string ProviderName { get; }
    bool CanHandle(EquityInstrumentKey key);
    Task<MarketDataResult<EquityQuote>> GetQuoteAsync(EquityInstrumentKey key, CancellationToken ct = default);
    Task<IReadOnlyList<MarketDataResult<EquityQuote>>> GetQuotesAsync(
        IReadOnlyList<EquityInstrumentKey> keys,
        CancellationToken ct = default);
}
