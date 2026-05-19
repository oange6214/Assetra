using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.MarketData;

internal sealed class TwelveDataQuoteProvider(
    TwelveDataClient client,
    IAppSettingsService settings,
    ITwelveDataQuotaTracker quotaTracker) : IEquityQuoteProvider, ITwelveDataConnectionTester
{
    private static readonly HashSet<string> SupportedExchanges = new(StringComparer.OrdinalIgnoreCase)
    {
        "NASDAQ",
        "NYSE",
        "NYSEARCA",
        "AMEX",
        "BATS",
        "IEX",
    };

    public string ProviderName => "Twelve Data";

    public bool CanHandle(EquityInstrumentKey key) => SupportedExchanges.Contains(key.Exchange);

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

        var apiKey = settings.Current.TwelveDataApiKey;
        var results = await client.FetchQuotesAsync(keys, apiKey, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(apiKey))
            await quotaTracker.RecordAsync(keys.Count, ct).ConfigureAwait(false);

        return results;
    }

    public async Task<MarketDataResult<EquityQuote>> TestAsync(string apiKey, CancellationToken ct = default)
    {
        var result = await client.FetchQuotesAsync([new EquityInstrumentKey("AAPL", "NASDAQ")], apiKey, ct)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(apiKey))
            await quotaTracker.RecordAsync(1, ct).ConfigureAwait(false);
        return result.First();
    }
}
