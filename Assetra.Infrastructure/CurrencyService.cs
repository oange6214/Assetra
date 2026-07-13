using System.Globalization;
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

    // 需向 Yahoo 抓即時匯率的外幣（TWD 為基準，不需自抓）。
    private static readonly IReadOnlyList<string> _fetchedCurrencies =
        ["USD", "JPY", "EUR", "HKD"];

    // 硬碼預設匯率（當 AppSettings.ExchangeRates 為 null 且尚未取得即時匯率時使用）
    private static readonly Dictionary<string, decimal> _hardcodedDefaults = new()
    {
        ["USD"] = 32.0m,
        ["JPY"] = 0.21m,
        ["EUR"] = 35.0m,
        ["HKD"] = 4.1m,
    };

    // Yahoo Finance 即時匯率端點：每個外幣對台幣一檔（{CODE}TWD=X）。
    // meta.regularMarketPrice 即「1 外幣 = N 台幣」，與台幣基準一致，毋須交叉換算。
    // （原台灣銀行當日 CSV 端點已被套上反爬蟲挑戰頁，一律回 HTML 解不出，故改走 Yahoo。）
    private const string YahooChartUrlFormat =
        "https://query1.finance.yahoo.com/v8/finance/chart/{0}TWD=X?interval=1d&range=1d";

    private readonly IAppSettingsService _settings;
    private readonly HttpClient _http;
    private readonly ILogger<CurrencyService> _logger;
    private volatile IReadOnlyDictionary<string, decimal> _exchangeRates;

    // 啟動初期網路/DNS 常還沒就緒 → 首次抓匯率會失敗、退回快取/預設值（每次開機都這樣）。
    // 重試幾次（遞增退避）讓網路就緒後補上。可注入（測試傳 Zero delay 以免拖慢）。
    private readonly int _fxMaxAttempts;
    private readonly TimeSpan _fxRetryBaseDelay;

    public string Currency { get; private set; }

    public decimal UsdTwdRate =>
        _exchangeRates.TryGetValue("USD", out var r) ? r : 32.0m;

    public IReadOnlyList<string> SupportedCurrencies => _supportedCurrencies;

    public IReadOnlyDictionary<string, decimal> ExchangeRates => _exchangeRates;

    public event Action? CurrencyChanged;

    public CurrencyService(IAppSettingsService settings, HttpClient http,
        ILogger<CurrencyService>? logger = null,
        int fxMaxAttempts = 3, TimeSpan? fxRetryBaseDelay = null)
    {
        _settings = settings;
        _http = http;
        _logger = logger ?? NullLogger<CurrencyService>.Instance;
        _fxMaxAttempts = Math.Max(1, fxMaxAttempts);
        _fxRetryBaseDelay = fxRetryBaseDelay ?? TimeSpan.FromSeconds(2);
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
        // 重試 _fxMaxAttempts 次（遞增退避）：啟動初期網路/DNS 常還沒就緒，首次會失敗；等一下重試就成。
        // 成功即 return；用完次數才退回快取/預設值。呼叫端主動取消（ct）則不重試、直接拋。
        for (var attempt = 1; attempt <= _fxMaxAttempts; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));

                // Yahoo 即時匯率：逐一抓 {CODE}TWD=X，取 regularMarketPrice。
                // 台幣為基準，Yahoo 直接報「1 外幣 = N 台幣」，毋須再做交叉匯率換算。
                var spotRates = await FetchYahooSpotRatesAsync(cts.Token).ConfigureAwait(false);

                if (spotRates is not null && spotRates.TryGetValue("USD", out var usd) && usd > 0m)
                {
                    // 以現有匯率為底，覆蓋成功解析（> 0）的幣別，缺的維持原值。
                    var newRates = _exchangeRates.ToDictionary(kv => kv.Key, kv => kv.Value);
                    foreach (var code in _fetchedCurrencies)
                        if (spotRates.TryGetValue(code, out var rate) && rate > 0m)
                            newRates[code] = rate;

                    _exchangeRates = newRates;

                    var updated = _settings.Current with
                    {
                        UsdTwdRate = newRates["USD"],
                        ExchangeRates = newRates,
                        LastFxRefreshUtc = DateTime.UtcNow.ToString("O"),
                    };
                    await _settings.SaveAsync(updated).ConfigureAwait(false);
                    CurrencyChanged?.Invoke();
                    return; // 成功
                }

                // 抓取／解析失敗或缺 USD（不寫入、保留現有值）→ 還有次數就退避重試。
                if (attempt >= _fxMaxAttempts)
                {
                    _logger.LogWarning(
                        "Yahoo FX rate unavailable after {Attempts} attempt(s); using cached/default rates",
                        _fxMaxAttempts);
                    return;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // 呼叫端主動取消 → 不吞、不重試
            }
            catch (Exception ex)
            {
                if (attempt >= _fxMaxAttempts)
                {
                    _logger.LogWarning(ex, "Exchange rate refresh failed after {Attempts} attempt(s); using cached/default rates", _fxMaxAttempts);
                    return;
                }
                _logger.LogDebug(ex, "Exchange rate refresh attempt {Attempt} failed; retrying", attempt);
            }

            // 走到這＝還要重試 → 遞增退避（base×attempt），等網路就緒。
            await Task.Delay(_fxRetryBaseDelay * attempt, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 逐一從 Yahoo Finance 抓各外幣對台幣的即時匯率（{CODE}TWD=X）。
    /// 單一幣別抓取／解析失敗只略過該幣別；全部皆失敗才回 null（呼叫端保留現有/預設匯率）。
    /// </summary>
    private async Task<IReadOnlyDictionary<string, decimal>?> FetchYahooSpotRatesAsync(CancellationToken ct)
    {
        var rates = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var code in _fetchedCurrencies)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var url = string.Format(CultureInfo.InvariantCulture, YahooChartUrlFormat, code);
                var json = await _http.GetStringAsync(url, ct).ConfigureAwait(false);
                if (ParseYahooChartPrice(json) is { } rate && rate > 0m)
                    rates[code] = rate;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // 呼叫端主動取消 → 不吞
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Yahoo FX fetch failed for {Code}TWD", code);
            }
        }
        return rates.Count > 0 ? rates : null;
    }

    /// <summary>
    /// 從 Yahoo chart JSON 取即時匯率：<c>chart.result[0].meta.regularMarketPrice</c>。
    /// 缺欄位／格式錯／值 ≤ 0 一律回 null（呼叫端視為此幣別無報價）。
    /// </summary>
    internal static decimal? ParseYahooChartPrice(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("chart", out var chart)
                && chart.TryGetProperty("result", out var result)
                && result.ValueKind == JsonValueKind.Array
                && result.GetArrayLength() > 0
                && result[0].TryGetProperty("meta", out var meta)
                && meta.TryGetProperty("regularMarketPrice", out var price)
                && price.ValueKind == JsonValueKind.Number
                && price.TryGetDecimal(out var rate)
                && rate > 0m)
                return rate;
        }
        catch (JsonException)
        {
            // 非 JSON（如反爬蟲 HTML 頁）→ 視為無報價
        }
        return null;
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
