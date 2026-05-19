using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.Http;

namespace Assetra.Infrastructure.MarketData;

internal sealed class TwseEquityQuoteProvider(ITwseClient client) : IEquityQuoteProvider
{
    public string ProviderName => "TWSE";

    public bool CanHandle(EquityInstrumentKey key) =>
        string.Equals(key.Exchange, "TWSE", StringComparison.OrdinalIgnoreCase);

    public async Task<MarketDataResult<EquityQuote>> GetQuoteAsync(
        EquityInstrumentKey key,
        CancellationToken ct = default)
    {
        var results = await GetQuotesAsync([key], ct).ConfigureAwait(false);
        return results.First();
    }

    public async Task<IReadOnlyList<MarketDataResult<EquityQuote>>> GetQuotesAsync(
        IReadOnlyList<EquityInstrumentKey> keys,
        CancellationToken ct = default)
    {
        if (keys.Count == 0)
            return [];

        var quotes = await client.FetchQuotesAsync(keys.Select(k => k.Symbol).Distinct()).ConfigureAwait(false);
        var byKey = quotes
            .GroupBy(q => new EquityInstrumentKey(q.Symbol, q.Exchange))
            .ToDictionary(
                g => g.Key,
                g => LegacyStockQuoteMapper.ToEquityQuoteResult(g.First(), ProviderName));

        return keys.Select(k =>
            byKey.GetValueOrDefault(k)
            ?? LegacyStockQuoteMapper.MissingQuote(k, ProviderName, $"TWSE did not return quote for {k.Symbol}."))
            .ToList();
    }
}
