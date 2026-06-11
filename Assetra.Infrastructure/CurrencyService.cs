using System.Text.Json;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Assetra.Infrastructure;

/// <summary>
/// 貨幣格式化與換算服務。
/// 所有金額輸入皆為台幣（TWD），切換其他貨幣時除以對應匯率。
/// </summary>
public sealed class CurrencyService : ICurrencyService
{
    private static readonly IReadOnlyList<string> _supportedCurrencies =
        ["TWD", "USD", "JPY", "EUR", "HKD"];

    // 硬碼預設匯率（當 AppSettings.ExchangeRates 為 null 且 Frankfurter 尚未回應時使用）
    private static readonly Dictionary<string, decimal> _hardcodedDefaults = new()
    {
        ["USD"] = 32.0m,
        ["JPY"] = 0.21m,
        ["EUR"] = 35.0m,
        ["HKD"] = 4.1m,
    };

    private readonly IAppSettingsService _settings;
    private readonly HttpClient _http;
    private readonly IFxRateHistoryFetcher _historyFetcher;
    private readonly ILogger<CurrencyService> _logger;
    private volatile IReadOnlyDictionary<string, decimal> _exchangeRates;

    public string Currency { get; private set; }

    public decimal UsdTwdRate =>
        _exchangeRates.TryGetValue("USD", out var r) ? r : 32.0m;

    public IReadOnlyList<string> SupportedCurrencies => _supportedCurrencies;

    public IReadOnlyDictionary<string, decimal> ExchangeRates => _exchangeRates;

    public event Action? CurrencyChanged;

    public CurrencyService(IAppSettingsService settings, HttpClient http,
        IFxRateHistoryFetcher historyFetcher,
        ILogger<CurrencyService>? logger = null)
    {
        _settings = settings;
        _http = http;
        _historyFetcher = historyFetcher;
        _logger = logger ?? NullLogger<CurrencyService>.Instance;
        Currency = settings.Current.PreferredCurrency;
        _exchangeRates = BuildRates(settings.Current);
    }

    public async Task ApplyAsync(string currency)
    {
        Currency = currency;
        var updated = _settings.Current with { PreferredCurrency = currency };
        await _settings.SaveAsync(updated).ConfigureAwait(false);
        CurrencyChanged?.Invoke();
    }

    public async Task RefreshRatesAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            // USD→TWD from Yahoo. Frankfurter (ECB) does NOT quote TWD, so the
            // critical base-currency rate comes from Yahoo's USDTWD=X pair.
            var usdToTwd = await FetchUsdTwdFromYahooAsync(cts.Token).ConfigureAwait(false);

            // USD→{JPY,EUR,HKD} cross-rates from Frankfurter (reliable for these).
            // A Frankfurter failure here is non-fatal — USD is the critical rate.
            var cross = await FetchCrossRatesFromFrankfurterAsync(cts.Token).ConfigureAwait(false);

            if (usdToTwd is not { } twd)
            {
                // Yahoo unavailable: do NOT fabricate a TWD rate. Leave the
                // current/default rates intact (same spirit as the old bail-out).
                _logger.LogWarning("Yahoo USD→TWD rate unavailable; using cached/default rates");
                return;
            }

            // Frankfurter returns: 1 USD = N {currency}
            // We store: 1 {foreign currency} = N TWD
            // So: TWD rate of foreign = usdToTwd / usdToForeign
            var newRates = _exchangeRates.ToDictionary(kv => kv.Key, kv => kv.Value);
            newRates["USD"] = twd;

            if (cross is not null)
            {
                if (cross.TryGetValue("JPY", out var usdToJpy) && usdToJpy > 0m)
                    newRates["JPY"] = Math.Round(twd / usdToJpy, 4);

                if (cross.TryGetValue("EUR", out var usdToEur) && usdToEur > 0m)
                    newRates["EUR"] = Math.Round(twd / usdToEur, 4);

                if (cross.TryGetValue("HKD", out var usdToHkd) && usdToHkd > 0m)
                    newRates["HKD"] = Math.Round(twd / usdToHkd, 4);
            }

            _exchangeRates = newRates;

            var updated = _settings.Current with
            {
                UsdTwdRate = newRates["USD"],
                ExchangeRates = newRates,
                LastFxRefreshUtc = DateTime.UtcNow.ToString("O"),
            };
            await _settings.SaveAsync(updated).ConfigureAwait(false);
            CurrencyChanged?.Invoke();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller cancelled — propagate rather than swallow
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exchange rate refresh failed; using cached/default rates");
        }
    }

    /// <summary>
    /// Latest USD→TWD from Yahoo (last 7 days, take the entry with MAX Date).
    /// Returns null when Yahoo returns no data. The fetcher swallows its own
    /// errors and returns an empty list per its contract, but we guard
    /// defensively all the same.
    /// </summary>
    private async Task<decimal?> FetchUsdTwdFromYahooAsync(CancellationToken ct)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var history = await _historyFetcher
                .FetchAsync("USD", "TWD", today.AddDays(-7), today, ct)
                .ConfigureAwait(false);

            if (history is null || history.Count == 0)
                return null;

            var latest = history.MaxBy(e => e.Date);
            return latest is { Rate: > 0m } ? latest.Rate : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Yahoo USD→TWD fetch failed");
            return null;
        }
    }

    /// <summary>
    /// USD→{JPY,EUR,HKD} cross-rates from Frankfurter. Returns null on any
    /// failure (non-fatal — caller keeps existing cross-rates).
    /// </summary>
    private async Task<IReadOnlyDictionary<string, decimal>?> FetchCrossRatesFromFrankfurterAsync(CancellationToken ct)
    {
        try
        {
            var url = "https://api.frankfurter.app/latest?from=USD&to=JPY,EUR,HKD";
            var json = await _http.GetStringAsync(url, ct).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("rates", out var rates) ||
                rates.ValueKind != JsonValueKind.Object)
            {
                _logger.LogWarning("Frankfurter response did not include a valid 'rates' object");
                return null;
            }

            var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var code in (ReadOnlySpan<string>)["JPY", "EUR", "HKD"])
                if (TryGetDecimalProperty(rates, code, out var rate))
                    result[code] = rate;

            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Frankfurter cross-rate fetch failed; keeping existing cross-rates");
            return null;
        }
    }

    private static bool TryGetDecimalProperty(JsonElement parent, string propertyName, out decimal value)
    {
        value = default;
        return parent.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetDecimal(out value);
    }

    // ── Formatting ──────────────────────────────────────────────────────────

    public string FormatAmount(decimal twdValue)
    {
        var value = Convert(twdValue);
        var sym = GetSymbol(Currency);
        return $"{sym}{value:N0}";
    }

    public string FormatPrice(decimal twdValue)
    {
        // 台股券商慣例：小數點 2 位無條件捨去（non-rounding truncation）
        var value = Convert(twdValue);
        var truncated = Math.Truncate(value * 100m) / 100m;
        var sym = GetSymbol(Currency);
        return $"{sym}{truncated:0.00}";
    }

    public string FormatSigned(decimal twdValue)
    {
        // 符號提前：sign → currency symbol → magnitude（例：-NT$30,000、+US$1,000）
        var value = Convert(twdValue);
        var sign = value >= 0 ? "+" : "-";
        var magnit = Math.Abs(value);
        var sym = GetSymbol(Currency);
        return $"{sign}{sym}{magnit:N0}";
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private decimal Convert(decimal twdValue)
    {
        if (Currency == "TWD")
            return twdValue;
        return _exchangeRates.TryGetValue(Currency, out var rate) && rate > 0
            ? twdValue / rate
            : twdValue;
    }

    private static string GetSymbol(string currency) => currency switch
    {
        "USD" => "US$",
        "JPY" => "¥",
        "EUR" => "€",
        "HKD" => "HK$",
        _ => "NT$",   // TWD and unknown
    };

    private static Dictionary<string, decimal> BuildRates(AppSettings s)
    {
        if (s.ExchangeRates is { Count: > 0 } persisted)
        {
            // Start from hardcoded defaults, then overlay valid persisted values.
            // Filters out zero/negative entries that could cause divide-by-zero in Convert().
            var result = new Dictionary<string, decimal>(_hardcodedDefaults);
            foreach (var (key, value) in persisted)
                if (value > 0m)
                    result[key] = value;
            return result;
        }

        // Cold start: seed from legacy UsdTwdRate if available
        var seed = new Dictionary<string, decimal>(_hardcodedDefaults);
        if (s.UsdTwdRate > 0)
            seed["USD"] = s.UsdTwdRate;
        return seed;
    }
}
