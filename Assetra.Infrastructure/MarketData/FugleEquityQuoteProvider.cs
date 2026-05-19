using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.Http;

namespace Assetra.Infrastructure.MarketData;

internal sealed class FugleEquityQuoteProvider(
    FugleClient client,
    IAppSettingsService settings) : IEquityQuoteProvider
{
    public string ProviderName => "Fugle";

    public bool CanHandle(EquityInstrumentKey key)
    {
        var isTaiwanExchange = string.Equals(key.Exchange, "TWSE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(key.Exchange, "TPEX", StringComparison.OrdinalIgnoreCase);
        return isTaiwanExchange
            && client.IsConfigured
            && string.Equals(settings.Current.QuoteProvider, "fugle", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<MarketDataResult<EquityQuote>> GetQuoteAsync(
        EquityInstrumentKey key,
        CancellationToken ct = default)
    {
        var quote = await client.FetchQuoteAsync(key.Symbol, ct).ConfigureAwait(false);
        if (quote is null)
        {
            return LegacyStockQuoteMapper.MissingQuote(
                key,
                ProviderName,
                $"Fugle did not return quote for {key.Symbol}.");
        }

        return LegacyStockQuoteMapper.ToEquityQuoteResult(quote, ProviderName);
    }

    public async Task<IReadOnlyList<MarketDataResult<EquityQuote>>> GetQuotesAsync(
        IReadOnlyList<EquityInstrumentKey> keys,
        CancellationToken ct = default)
    {
        if (keys.Count == 0)
            return [];

        var results = await Task.WhenAll(keys.Select(k => GetQuoteAsync(k, ct))).ConfigureAwait(false);
        return results;
    }
}
