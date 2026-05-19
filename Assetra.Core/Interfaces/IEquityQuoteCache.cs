using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IEquityQuoteCache
{
    bool TryGet(
        EquityInstrumentKey key,
        TimeSpan maxAge,
        DateTimeOffset now,
        out EquityQuote quote);

    void Store(EquityQuote quote, DateTimeOffset now);
}
