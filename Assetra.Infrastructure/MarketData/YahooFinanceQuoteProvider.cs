using System.Text.Json;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.History;

namespace Assetra.Infrastructure.MarketData;

/// <summary>
/// Yahoo Finance fallback quote provider for foreign equities (US / HK / JP).
/// Uses the same v8/chart endpoint as <see cref="YahooFinanceHistoryProvider"/>,
/// asking for <c>interval=1d&amp;range=1d</c>; the <c>meta</c> block returned
/// contains the current / last-close price without needing the v7/quote endpoint
/// (which requires a crumb cookie since 2024).
///
/// <para>
/// Registered after <see cref="TwelveDataQuoteProvider"/> in the DI container so
/// <see cref="Application.MarketData.EquityRouter"/> only falls back to Yahoo
/// when Twelve Data either:
/// <list type="bullet">
///   <item>Has no data for the symbol (newer / niche ETFs are sometimes paid-tier only)</item>
///   <item>Times out under bad network conditions even after retry</item>
///   <item>Has its quota exhausted (free tier: 800 requests / day)</item>
/// </list>
/// Yahoo Finance is free, requires no API key, and covers virtually every
/// publicly traded US / HK / JP symbol — perfect safety net.
/// </para>
/// </summary>
internal sealed class YahooFinanceQuoteProvider : IEquityQuoteProvider
{
    private readonly HttpClient _http;

    public YahooFinanceQuoteProvider(HttpClient http)
    {
        _http = http;
    }

    public string ProviderName => "Yahoo Finance";

    /// <summary>
    /// All foreign exchanges that YahooSymbolMapper can map. Taiwan venues
    /// (TWSE/TPEX) stay out — TwseEquityQuoteProvider / FugleEquityQuoteProvider
    /// handle those primary, with TWSE official as the canonical source.
    /// </summary>
    public bool CanHandle(EquityInstrumentKey key) =>
        YahooSymbolMapper.IsForeignExchange(key.Exchange);

    public async Task<MarketDataResult<EquityQuote>> GetQuoteAsync(
        EquityInstrumentKey key,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        try
        {
            var yahooSymbol = YahooSymbolMapper.ToYahooSymbol(key.Symbol, key.Exchange);
            var url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(yahooSymbol)}?interval=1d&range=1d";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            req.Headers.TryAddWithoutValidation("Accept-Language", "zh-TW,zh;q=0.9,en-US;q=0.8");
            using var response = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return MarketDataResult<EquityQuote>.Failure(new MarketDataError(
                    MarketDataErrorCode.ProviderUnavailable,
                    $"Yahoo Finance returned HTTP {(int)response.StatusCode}.",
                    Provider: ProviderName,
                    Instrument: key,
                    IsRetryable: true));
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseQuote(json, key);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return MarketDataResult<EquityQuote>.Failure(new MarketDataError(
                MarketDataErrorCode.NetworkFailure,
                $"Yahoo Finance request failed: {ex.Message}",
                Provider: ProviderName,
                Instrument: key,
                IsRetryable: true));
        }
    }

    public async Task<IReadOnlyList<MarketDataResult<EquityQuote>>> GetQuotesAsync(
        IReadOnlyList<EquityInstrumentKey> keys,
        CancellationToken ct = default)
    {
        // Yahoo's v8/chart endpoint is single-symbol per request. The shared HttpClient
        // has connection pooling, so sequential calls reuse sockets. Parallel would be
        // faster but risks rate-limiting (Yahoo doesn't publish limits but throttles
        // bursts). For a typical portfolio (< 50 US positions) sequential is fine.
        if (keys is null || keys.Count == 0)
            return [];

        var results = new List<MarketDataResult<EquityQuote>>(keys.Count);
        foreach (var key in keys)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await GetQuoteAsync(key, ct).ConfigureAwait(false));
        }
        return results;
    }

    /// <summary>
    /// Parses the chart endpoint's <c>meta</c> block — Yahoo packs the current and
    /// previous-close prices there so we don't need to walk the indicators array.
    /// </summary>
    private static MarketDataResult<EquityQuote> ParseQuote(string json, EquityInstrumentKey key)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return MarketDataResult<EquityQuote>.Failure(new MarketDataError(
                MarketDataErrorCode.InvalidResponse,
                "Yahoo Finance returned an empty response.",
                Provider: "Yahoo Finance",
                Instrument: key));
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("chart", out var chart))
                return InvalidShape(key);

            // {"chart":{"error":{...},"result":null}}
            if (chart.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object)
            {
                var errMsg = error.TryGetProperty("description", out var desc) ? desc.GetString() : "Yahoo Finance error";
                return MarketDataResult<EquityQuote>.Failure(new MarketDataError(
                    MarketDataErrorCode.UnsupportedSymbol,
                    errMsg ?? "Yahoo Finance error",
                    Provider: "Yahoo Finance",
                    Instrument: key));
            }

            if (!chart.TryGetProperty("result", out var resultArr)
                || resultArr.ValueKind != JsonValueKind.Array
                || resultArr.GetArrayLength() == 0)
            {
                return InvalidShape(key);
            }

            var first = resultArr[0];
            if (!first.TryGetProperty("meta", out var meta))
                return InvalidShape(key);

            var price = ReadDecimal(meta, "regularMarketPrice");
            if (price is null)
                return InvalidShape(key);

            var prevClose = ReadDecimal(meta, "chartPreviousClose") ?? ReadDecimal(meta, "previousClose");
            var currency = ReadString(meta, "currency");
            var instrumentSymbol = ReadString(meta, "symbol");
            var name = ReadString(meta, "longName") ?? ReadString(meta, "shortName");
            var updatedAt = ReadUpdatedAt(meta);

            // Mirror TwelveDataClient's resolved-key pattern — Yahoo returns the venue's
            // canonical symbol (may include .TW / .TWO / .HK / .T suffix). Keep the original
            // caller key so EquityRouter / FindQuoteTarget match on app's exchange tag.
            var change = prevClose is { } p ? price - p : (decimal?)null;
            var changePct = (prevClose is { } pp && pp > 0)
                ? (price - pp) / pp * 100m
                : (decimal?)null;

            return MarketDataResult<EquityQuote>.Success(new EquityQuote(
                key,
                price.Value,
                prevClose,
                change,
                changePct,
                string.IsNullOrWhiteSpace(currency)
                    ? StockExchangeRegistry.ResolveDefaultCurrency(key.Exchange, "USD")
                    : currency!,
                updatedAt,
                "Yahoo Finance",
                isDelayed: true,
                name ?? string.Empty));
        }
        catch (JsonException ex)
        {
            return MarketDataResult<EquityQuote>.Failure(new MarketDataError(
                MarketDataErrorCode.InvalidResponse,
                $"Yahoo Finance returned invalid JSON: {ex.Message}",
                Provider: "Yahoo Finance",
                Instrument: key));
        }
    }

    private static MarketDataResult<EquityQuote> InvalidShape(EquityInstrumentKey key) =>
        MarketDataResult<EquityQuote>.Failure(new MarketDataError(
            MarketDataErrorCode.InvalidResponse,
            "Yahoo Finance response shape unexpected.",
            Provider: "Yahoo Finance",
            Instrument: key));

    private static decimal? ReadDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var n))
            return n;
        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        return decimal.TryParse(text, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString();
        return value.ToString();
    }

    private static DateTimeOffset ReadUpdatedAt(JsonElement meta)
    {
        if (meta.TryGetProperty("regularMarketTime", out var ts)
            && ts.ValueKind == JsonValueKind.Number
            && ts.TryGetInt64(out var seconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        return DateTimeOffset.UtcNow;
    }
}
