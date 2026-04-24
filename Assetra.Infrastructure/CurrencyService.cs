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
    private readonly ILogger<CurrencyService> _logger;
    private volatile IReadOnlyDictionary<string, decimal> _exchangeRates;

    public string Currency { get; private set; }

    public decimal UsdTwdRate =>
        _exchangeRates.TryGetValue("USD", out var r) ? r : 32.0m;

    public IReadOnlyList<string> SupportedCurrencies => _supportedCurrencies;

    public IReadOnlyDictionary<string, decimal> ExchangeRates => _exchangeRates;

    public event Action? CurrencyChanged;

    public CurrencyService(IAppSettingsService settings, HttpClient http,
        ILogger<CurrencyService>? logger = null)
    {
        _settings = settings;
        _http = http;
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

            // Frankfurter: base USD → TWD, JPY, EUR, HKD
            var url = "https://api.frankfurter.app/latest?from=USD&to=TWD,JPY,EUR,HKD";
            var json = await _http.GetStringAsync(url, cts.Token).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("rates", out var rates) ||
                rates.ValueKind != JsonValueKind.Object)
            {
                _logger.LogWarning("Frankfurter response did not include a valid 'rates' object");
                return;
            }

            var newRates = _exchangeRates.ToDictionary(kv => kv.Key, kv => kv.Value);

            // Frankfurter returns: 1 USD = N {currency}
            // We store: 1 {foreign currency} = N TWD
            // So: TWD rate of foreign = usdToTwd / usdToForeign
            if (!TryGetDecimalProperty(rates, "TWD", out var usdToTwd))
            {
                _logger.LogWarning("Frankfurter response did not include a valid TWD rate");
                return;
            }

            newRates["USD"] = usdToTwd;

            if (TryGetDecimalProperty(rates, "JPY", out var usdToJpy) && usdToJpy > 0m)
                newRates["JPY"] = Math.Round(usdToTwd / usdToJpy, 4);

            if (TryGetDecimalProperty(rates, "EUR", out var usdToEur) && usdToEur > 0m)
                newRates["EUR"] = Math.Round(usdToTwd / usdToEur, 4);

            if (TryGetDecimalProperty(rates, "HKD", out var usdToHkd) && usdToHkd > 0m)
                newRates["HKD"] = Math.Round(usdToTwd / usdToHkd, 4);

            _exchangeRates = newRates;

            var updated = _settings.Current with
            {
                UsdTwdRate = newRates["USD"],
                ExchangeRates = newRates,
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
            _logger.LogWarning(ex, "Frankfurter exchange rate fetch failed; using cached/default rates");
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
        // 符號提前：sign → currency symbol → magnitude（例：-NT$30,000、+$1,000）
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
        "USD" => "$",
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
