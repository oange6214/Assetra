using System.Globalization;
using System.Text.Json;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.MarketData;

internal sealed class TwelveDataClient(HttpClient http)
{
    private const string BaseUrl = "https://api.twelvedata.com/quote";
    private const string ProviderName = "Twelve Data";

    // Twelve Data free tier 有時 cold-start > 20s (shared HttpClient 的全域 timeout)。
    // 重試策略：最多 3 次 attempts，只對 timeout / network 錯誤重試，不對 401/404/rate-limit 重試。
    // backoff：500ms, 1500ms。最壞情況一個 group 60s 內結束（20s × 3 + delays），
    // 仍在 scheduler 10s 間隔的容忍範圍內（reactive pipeline 不會排隊）。
    private const int MaxAttempts = 3;
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(1500)];

    public async Task<IReadOnlyList<MarketDataResult<EquityQuote>>> FetchQuotesAsync(
        IReadOnlyList<EquityInstrumentKey> keys,
        string apiKey,
        CancellationToken ct = default)
    {
        if (keys.Count == 0)
            return [];

        if (string.IsNullOrWhiteSpace(apiKey))
            return keys.Select(k => Failure(k, MarketDataErrorCode.MissingApiKey, "Twelve Data API key is missing.")).ToList();

        var results = new Dictionary<EquityInstrumentKey, MarketDataResult<EquityQuote>>();
        foreach (var group in keys.GroupBy(k => k.Exchange, StringComparer.OrdinalIgnoreCase))
        {
            var groupKeys = group.ToList();
            var uri = BuildQuoteUri(groupKeys, apiKey);

            Exception? lastException = null;
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    using var response = await http.GetAsync(uri, ct).ConfigureAwait(false);
                    var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    var parsed = ParseResponse(json, groupKeys);
                    foreach (var item in parsed)
                        results[item.Key] = item.Value;
                    lastException = null;
                    break; // success — leave retry loop
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // External cancellation (app shutdown / scheduler stop) — don't retry, bubble up.
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    lastException = ex;
                    if (attempt < MaxAttempts)
                    {
                        try
                        {
                            await Task.Delay(RetryDelays[attempt - 1], ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                    }
                }
            }

            if (lastException is not null)
            {
                foreach (var key in groupKeys)
                {
                    results[key] = Failure(
                        key,
                        MarketDataErrorCode.NetworkFailure,
                        $"Twelve Data request failed after {MaxAttempts} attempts: {lastException.Message}",
                        isRetryable: true);
                }
            }
        }

        return keys.Select(k =>
            results.GetValueOrDefault(k)
            ?? Failure(k, MarketDataErrorCode.UnsupportedSymbol, $"Twelve Data did not return quote for {k.Symbol}."))
            .ToList();
    }

    private static Uri BuildQuoteUri(IReadOnlyList<EquityInstrumentKey> keys, string apiKey)
    {
        var exchange = keys.Select(k => k.Exchange).Distinct(StringComparer.OrdinalIgnoreCase).Single();
        var parameters = new Dictionary<string, string>
        {
            ["symbol"] = string.Join(",", keys.Select(k => k.Symbol).Distinct(StringComparer.OrdinalIgnoreCase)),
            ["apikey"] = apiKey.Trim(),
            ["format"] = "JSON",
        };

        var providerExchange = ToProviderExchange(exchange);
        if (!string.IsNullOrWhiteSpace(providerExchange))
            parameters["exchange"] = providerExchange;

        var query = string.Join("&", parameters.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        return new Uri($"{BaseUrl}?{query}");
    }

    private static IReadOnlyDictionary<EquityInstrumentKey, MarketDataResult<EquityQuote>> ParseResponse(
        string json,
        IReadOnlyList<EquityInstrumentKey> requestedKeys)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return requestedKeys.ToDictionary(
                k => k,
                k => Failure(k, MarketDataErrorCode.InvalidResponse, "Twelve Data returned an empty response."));
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (IsError(root))
                return requestedKeys.ToDictionary(k => k, k => FailureFromError(root, k));

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("symbol", out _))
            {
                var key = requestedKeys.Count == 1
                    ? requestedKeys[0]
                    : ResolveKeyForObject(root, requestedKeys) ?? requestedKeys[0];
                return new Dictionary<EquityInstrumentKey, MarketDataResult<EquityQuote>>
                {
                    [key] = ParseQuote(root, key),
                };
            }

            var bySearchKey = new Dictionary<string, MarketDataResult<EquityQuote>>(StringComparer.OrdinalIgnoreCase);
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in root.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Object)
                        continue;

                    var key = requestedKeys.FirstOrDefault(k =>
                        EquitySymbolNormalizer.SymbolMatches(k.Symbol, property.Name))
                        ?? ResolveKeyForObject(property.Value, requestedKeys);
                    if (key is null)
                        continue;

                    bySearchKey[EquitySymbolNormalizer.ToSearchKey(key.Symbol)] = ParseQuote(property.Value, key);
                }
            }

            return requestedKeys.ToDictionary(
                k => k,
                k => bySearchKey.GetValueOrDefault(EquitySymbolNormalizer.ToSearchKey(k.Symbol))
                    ?? Failure(k, MarketDataErrorCode.UnsupportedSymbol, $"Twelve Data did not return quote for {k.Symbol}."));
        }
        catch (JsonException ex)
        {
            return requestedKeys.ToDictionary(
                k => k,
                k => Failure(k, MarketDataErrorCode.InvalidResponse, $"Twelve Data returned invalid JSON: {ex.Message}"));
        }
    }

    private static MarketDataResult<EquityQuote> ParseQuote(JsonElement element, EquityInstrumentKey fallbackKey)
    {
        if (IsError(element))
            return FailureFromError(element, fallbackKey);

        var symbol = ReadString(element, "symbol");
        var exchange = ReadString(element, "exchange");
        var key = new EquityInstrumentKey(
            string.IsNullOrWhiteSpace(symbol) ? fallbackKey.Symbol : symbol,
            string.IsNullOrWhiteSpace(exchange) ? fallbackKey.Exchange : exchange);

        var price = ReadDecimal(element, "close") ?? ReadDecimal(element, "price") ?? ReadDecimal(element, "last");
        if (price is null)
            return Failure(key, MarketDataErrorCode.InvalidResponse, $"Twelve Data quote for {key.Symbol} has no price.");

        var previousClose = ReadDecimal(element, "previous_close");
        var change = ReadDecimal(element, "change");
        var changePercent = ReadDecimal(element, "percent_change");
        var currency = ReadString(element, "currency");
        var updatedAt = ReadUpdatedAt(element);
        var name = ReadString(element, "name");

        return MarketDataResult<EquityQuote>.Success(new EquityQuote(
            key,
            price.Value,
            previousClose,
            change,
            changePercent,
            string.IsNullOrWhiteSpace(currency) ? StockExchangeRegistry.ResolveDefaultCurrency(key.Exchange, "USD") : currency,
            updatedAt,
            ProviderName,
            isDelayed: true,
            name));
    }

    private static EquityInstrumentKey? ResolveKeyForObject(JsonElement element, IReadOnlyList<EquityInstrumentKey> keys)
    {
        var symbol = ReadString(element, "symbol");
        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        var exchange = EquitySymbolNormalizer.NormalizeExchange(ReadString(element, "exchange"));
        return keys.FirstOrDefault(k =>
            EquitySymbolNormalizer.SymbolMatches(k.Symbol, symbol) &&
            (exchange.Length == 0 || string.Equals(k.Exchange, exchange, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsError(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty("status", out var status) &&
               string.Equals(status.GetString(), "error", StringComparison.OrdinalIgnoreCase);
    }

    private static MarketDataResult<EquityQuote> FailureFromError(JsonElement error, EquityInstrumentKey key)
    {
        var message = ReadString(error, "message");
        if (string.IsNullOrWhiteSpace(message))
            message = "Twelve Data returned an error.";

        return Failure(key, ClassifyError(error, message), message, IsRetryableError(message));
    }

    private static MarketDataErrorCode ClassifyError(JsonElement error, string message)
    {
        var code = ReadInt(error, "code");
        var text = message.ToLowerInvariant();
        if (code is 401 || text.Contains("apikey", StringComparison.Ordinal) || text.Contains("api key", StringComparison.Ordinal))
            return MarketDataErrorCode.MissingApiKey;
        if (text.Contains("credit", StringComparison.Ordinal) || text.Contains("quota", StringComparison.Ordinal))
            return MarketDataErrorCode.QuotaExceeded;
        if (code is 429 || text.Contains("rate limit", StringComparison.Ordinal) || text.Contains("too many", StringComparison.Ordinal))
            return MarketDataErrorCode.RateLimited;
        if (text.Contains("not found", StringComparison.Ordinal) || text.Contains("symbol", StringComparison.Ordinal))
            return MarketDataErrorCode.UnsupportedSymbol;
        return MarketDataErrorCode.InvalidResponse;
    }

    private static bool IsRetryableError(string message)
    {
        var text = message.ToLowerInvariant();
        return text.Contains("rate limit", StringComparison.Ordinal)
            || text.Contains("too many", StringComparison.Ordinal)
            || text.Contains("temporarily", StringComparison.Ordinal);
    }

    private static MarketDataResult<EquityQuote> Failure(
        EquityInstrumentKey key,
        MarketDataErrorCode code,
        string message,
        bool isRetryable = false)
    {
        return MarketDataResult<EquityQuote>.Failure(new MarketDataError(
            code,
            message,
            Provider: ProviderName,
            Instrument: key,
            IsRetryable: isRetryable));
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.String
                ? value.GetString()?.Trim() ?? string.Empty
                : value.ToString().Trim()
            : string.Empty;
    }

    private static decimal? ReadDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number;

        var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        return int.TryParse(value.ToString(), CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static DateTimeOffset ReadUpdatedAt(JsonElement element)
    {
        if (element.TryGetProperty("timestamp", out var timestamp) &&
            timestamp.ValueKind == JsonValueKind.Number &&
            timestamp.TryGetInt64(out var seconds))
            return DateTimeOffset.FromUnixTimeSeconds(seconds);

        var datetime = ReadString(element, "datetime");
        return DateTimeOffset.TryParse(datetime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;
    }

    private static string ToProviderExchange(string exchange) =>
        exchange.ToUpperInvariant() switch
        {
            "NYSEARCA" => "NYSE ARCA",
            "AMEX" => "NYSE American",
            _ => exchange,
        };
}
