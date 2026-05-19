using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IEquityRouter
{
    Task<MarketDataResult<EquityQuote>> GetQuoteAsync(
        EquityInstrumentKey key,
        CancellationToken ct = default);

    Task<MarketDataResult<EquityQuote>> GetQuoteAsync(
        EquityInstrumentKey key,
        TimeSpan maxAge,
        CancellationToken ct = default);

    Task<IReadOnlyList<MarketDataResult<EquityQuote>>> GetQuotesAsync(
        IReadOnlyList<EquityInstrumentKey> keys,
        CancellationToken ct = default);

    Task<IReadOnlyList<MarketDataResult<EquityQuote>>> GetQuotesAsync(
        IReadOnlyList<EquityInstrumentKey> keys,
        TimeSpan maxAge,
        CancellationToken ct = default);
}
