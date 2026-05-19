using System.Collections.Concurrent;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.MarketData;

public sealed class InMemoryEquityQuoteCache : IEquityQuoteCache
{
    private readonly ConcurrentDictionary<EquityInstrumentKey, Entry> _entries = new();

    public bool TryGet(
        EquityInstrumentKey key,
        TimeSpan maxAge,
        DateTimeOffset now,
        out EquityQuote quote)
    {
        ArgumentNullException.ThrowIfNull(key);

        quote = default!;
        if (maxAge <= TimeSpan.Zero)
            return false;

        if (!_entries.TryGetValue(key, out var entry))
            return false;

        if (now - entry.StoredAt > maxAge)
            return false;

        quote = entry.Quote;
        return true;
    }

    public void Store(EquityQuote quote, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(quote);
        _entries[quote.Instrument] = new Entry(quote, now);
    }

    private sealed record Entry(EquityQuote Quote, DateTimeOffset StoredAt);
}
