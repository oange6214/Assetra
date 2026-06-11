using System.Globalization;
using System.Text;
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

    // 透過台灣銀行抓的外幣（TWD 為基準，不需自抓）。
    private static readonly IReadOnlyList<string> _fetchedCurrencies =
        ["USD", "JPY", "EUR", "HKD"];

    // 硬碼預設匯率（當 AppSettings.ExchangeRates 為 null 且台灣銀行尚未回應時使用）
    private static readonly Dictionary<string, decimal> _hardcodedDefaults = new()
    {
        ["USD"] = 32.0m,
        ["JPY"] = 0.21m,
        ["EUR"] = 35.0m,
        ["HKD"] = 4.1m,
    };

    // 台灣銀行即時匯率（當日，所有幣別一檔 CSV）。每列一個幣別，即期買入 = 第 3 欄。
    private const string BotDailyCsvUrl = "https://rate.bot.com.tw/xrt/flcsv/0/day";

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

            // 台灣銀行當日匯率：一檔 CSV 含全部幣別，採「即期買入」（spot-buy）。
            // 台幣為基準，BOT 直接報「1 外幣 = N 台幣」，毋須再做交叉匯率換算。
            var spotBuy = await FetchBotSpotBuyAsync(cts.Token).ConfigureAwait(false);

            // USD 為關鍵基準匯率：抓取失敗或缺 USD 時不寫入，保留現有/預設值。
            if (spotBuy is null || !spotBuy.TryGetValue("USD", out var usd) || usd <= 0m)
            {
                _logger.LogWarning("Bank of Taiwan spot-buy rate unavailable; using cached/default rates");
                return;
            }

            // 以現有匯率為底，覆蓋成功解析（> 0）的幣別，缺的維持原值。
            var newRates = _exchangeRates.ToDictionary(kv => kv.Key, kv => kv.Value);
            foreach (var code in _fetchedCurrencies)
                if (spotBuy.TryGetValue(code, out var rate) && rate > 0m)
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
    /// 抓取台灣銀行當日匯率 CSV 並解析出各幣別的即期買入價。
    /// 回傳 null 代表抓取／解析整體失敗（呼叫端保留現有/預設匯率）。
    /// </summary>
    private async Task<IReadOnlyDictionary<string, decimal>?> FetchBotSpotBuyAsync(CancellationToken ct)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(BotDailyCsvUrl, ct).ConfigureAwait(false);
            var csv = Encoding.UTF8.GetString(bytes);
            return ParseBotDailyCsv(csv);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bank of Taiwan daily CSV fetch failed");
            return null;
        }
    }

    /// <summary>
    /// 解析台灣銀行「當日」匯率 CSV（<c>/xrt/flcsv/0/day</c>）為 code→即期買入 字典。
    /// <para>欄位（0-indexed）：[0]=幣別代碼, [1]="本行買入", [2]=現金買入,
    /// [3]=即期買入, …, [11]="本行賣出", …。即期買入 = col[3]。</para>
    /// <para>第一列為標題（首欄含 BOM），略過；即期買入非數字／≤ 0 的幣別跳過。
    /// BOT 每一幣別皆以「1 單位 = N 台幣」報價（USD≈31.5、JPY≈0.21、EUR≈36.5、HKD≈4.0）。</para>
    /// </summary>
    internal static Dictionary<string, decimal> ParseBotDailyCsv(string csv)
    {
        var result = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(csv))
            return result;

        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // 第 0 列為標題列（幣別,匯率,現金,即期,…），略過。
        for (int i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split(',');
            if (cols.Length <= 3)
                continue;
            var code = cols[0].Trim().ToUpperInvariant();
            if (code.Length == 0)
                continue;
            if (decimal.TryParse(cols[3].Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var spotBuy)
                && spotBuy > 0m)
                result[code] = spotBuy;
        }
        return result;
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
