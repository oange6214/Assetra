using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.MarketData;

internal sealed class NullEquityQuoteProvider : IEquityQuoteProvider
{
    public string ProviderName => "Null";

    public bool CanHandle(EquityInstrumentKey key) => true;

    public Task<MarketDataResult<EquityQuote>> GetQuoteAsync(
        EquityInstrumentKey key,
        CancellationToken ct = default)
    {
        return Task.FromResult(Failure(key));
    }

    public Task<IReadOnlyList<MarketDataResult<EquityQuote>>> GetQuotesAsync(
        IReadOnlyList<EquityInstrumentKey> keys,
        CancellationToken ct = default)
    {
        IReadOnlyList<MarketDataResult<EquityQuote>> results = keys.Select(Failure).ToList();
        return Task.FromResult(results);
    }

    private MarketDataResult<EquityQuote> Failure(EquityInstrumentKey key) =>
        MarketDataResult<EquityQuote>.Failure(new MarketDataError(
            MarketDataErrorCode.UnsupportedSymbol,
            $"No quote provider is configured for {key}.",
            Provider: ProviderName,
            Instrument: key));
}
