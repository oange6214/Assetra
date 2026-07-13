using System.Globalization;
using System.Net;
using System.Net.Http;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Assetra.Tests.Infrastructure;

public class CurrencyServiceTests
{
    private static readonly Dictionary<string, decimal> DefaultRates = new()
    {
        ["USD"] = 32.0m,
        ["JPY"] = 0.21m,
        ["EUR"] = 35.0m,
        ["HKD"] = 4.1m,
    };

    /// <summary>
    /// Yahoo live quotes for each {CODE}TWD=X pair, deliberately distinct from
    /// <see cref="DefaultRates"/> so a successful refresh is observable. Yahoo quotes
    /// "1 foreign = N TWD" directly (USD≈31.5, JPY≈0.196, EUR≈36.7, HKD≈4.05 — per-1-unit,
    /// NOT per-100), matching the TWD base with no cross-rate division.
    /// </summary>
    private static readonly Dictionary<string, decimal> YahooRates = new()
    {
        ["USD"] = 31.5m,
        ["JPY"] = 0.196m,
        ["EUR"] = 36.72m,
        ["HKD"] = 4.05m,
    };

    /// <summary>A minimal Yahoo chart response carrying <c>meta.regularMarketPrice</c>.</summary>
    private static string YahooChartJson(decimal price) =>
        "{\"chart\":{\"result\":[{\"meta\":{\"regularMarketPrice\":"
        + price.ToString(CultureInfo.InvariantCulture)
        + "}}],\"error\":null}}";

    /// <summary>Pull the currency code out of a <c>.../chart/{CODE}TWD=X?...</c> URL.</summary>
    private static string? ExtractPairCode(string url)
    {
        const string marker = "/chart/";
        var i = url.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0)
            return null;
        var rest = url[(i + marker.Length)..];
        var j = rest.IndexOf("TWD=X", StringComparison.Ordinal);
        return j > 0 ? rest[..j] : null;
    }

    private static CurrencyService Create(
        string currency = "TWD",
        decimal usdRate = 32.0m,
        Dictionary<string, decimal>? rates = null,
        HttpClient? http = null)
    {
        var settings = new Mock<IAppSettingsService>();
        settings.SetupGet(s => s.Current)
            .Returns(new AppSettings(
                PreferredCurrency: currency,
                UsdTwdRate: usdRate,
                ExchangeRates: rates));
        settings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>(), It.IsAny<bool>()))
            .Returns(Task.CompletedTask);
        http ??= new HttpClient();
        return new CurrencyService(settings.Object, http, NullLogger<CurrencyService>.Instance,
            fxRetryBaseDelay: TimeSpan.Zero); // 測試不等退避
    }

    // ── SupportedCurrencies ────────────────────────────────────────────────

    [Fact]
    public void SupportedCurrencies_ContainsFiveCurrencies()
    {
        var svc = Create();
        Assert.Equal(new[] { "TWD", "USD", "JPY", "EUR", "HKD" }, svc.SupportedCurrencies);
    }

    // ── ExchangeRates default fallback ─────────────────────────────────────

    [Fact]
    public void ExchangeRates_WhenNullInSettings_ReturnsHardcodedDefaults()
    {
        var svc = Create(rates: null);
        Assert.Equal(32.0m, svc.ExchangeRates["USD"]);
        Assert.Equal(0.21m, svc.ExchangeRates["JPY"]);
        Assert.Equal(35.0m, svc.ExchangeRates["EUR"]);
        Assert.Equal(4.1m, svc.ExchangeRates["HKD"]);
    }

    [Fact]
    public void ExchangeRates_WhenPresentInSettings_UsesPersistedValues()
    {
        var persisted = new Dictionary<string, decimal>
        {
            ["USD"] = 33.5m,
            ["JPY"] = 0.22m,
            ["EUR"] = 36.0m,
            ["HKD"] = 4.3m,
        };
        var svc = Create(rates: persisted);
        Assert.Equal(33.5m, svc.ExchangeRates["USD"]);
        Assert.Equal(0.22m, svc.ExchangeRates["JPY"]);
    }

    // ── UsdTwdRate backward-compat ─────────────────────────────────────────

    [Fact]
    public void UsdTwdRate_ReflectsExchangeRatesUsdEntry()
    {
        var svc = Create(rates: DefaultRates);
        Assert.Equal(32.0m, svc.UsdTwdRate);
    }

    // ── FormatSigned: 符號在貨幣符號之前 ─────────────────────────────────

    [Fact]
    public void FormatSigned_Positive_TWD_PrependsPlusBeforeCurrency()
    {
        var svc = Create("TWD");
        Assert.Equal("+NT$30,000", svc.FormatSigned(30_000m));
    }

    [Fact]
    public void FormatSigned_Negative_TWD_PrependsMinusBeforeCurrency()
    {
        var svc = Create("TWD");
        Assert.Equal("-NT$30,000", svc.FormatSigned(-30_000m));
    }

    [Fact]
    public void FormatSigned_Zero_IsPositiveSign()
    {
        var svc = Create("TWD");
        Assert.Equal("+NT$0", svc.FormatSigned(0m));
    }

    [Fact]
    public void FormatSigned_Negative_USD_PrependsMinusBeforeCurrency()
    {
        var svc = Create("USD", rates: DefaultRates);
        Assert.Equal("-US$1,000", svc.FormatSigned(-32_000m));
    }

    [Fact]
    public void FormatSigned_Positive_USD_PrependsPlusBeforeCurrency()
    {
        var svc = Create("USD", rates: DefaultRates);
        Assert.Equal("+US$1,000", svc.FormatSigned(32_000m));
    }

    [Fact]
    public void FormatSigned_Positive_JPY_UsesYenSymbol()
    {
        var svc = Create("JPY", rates: DefaultRates);
        var result = svc.FormatSigned(30_000m);
        Assert.StartsWith("+¥", result);
    }

    [Fact]
    public void FormatSigned_Positive_EUR_UsesEuroSymbol()
    {
        var svc = Create("EUR", rates: DefaultRates);
        // 35,000 TWD / 35.0 = 1,000 EUR
        Assert.Equal("+€1,000", svc.FormatSigned(35_000m));
    }

    [Fact]
    public void FormatSigned_Positive_HKD_UsesHkdSymbol()
    {
        var svc = Create("HKD", rates: DefaultRates);
        // 4,100 TWD / 4.1 = 1,000 HKD
        Assert.Equal("+HK$1,000", svc.FormatSigned(4_100m));
    }

    // ── FormatAmount ──────────────────────────────────────────────────────

    [Fact]
    public void FormatAmount_Positive_TWD_NoSignPrefix()
    {
        var svc = Create("TWD");
        Assert.Equal("NT$30,000", svc.FormatAmount(30_000m));
    }

    [Fact]
    public void FormatAmount_JPY_UsesYenSymbol()
    {
        var svc = Create("JPY", rates: DefaultRates);
        // 2,100 TWD / 0.21 = 10,000 JPY
        Assert.Equal("¥10,000", svc.FormatAmount(2_100m));
    }

    // ── ApplyAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_ChangesCurrencyAndFiresEvent()
    {
        var svc = Create("TWD", rates: DefaultRates);
        bool fired = false;
        svc.CurrencyChanged += () => fired = true;
        await svc.ApplyAsync("USD");
        Assert.Equal("USD", svc.Currency);
        Assert.True(fired);
    }

    // ── RefreshRatesAsync: Yahoo regularMarketPrice ────────────────────────

    [Fact]
    public async Task RefreshRatesAsync_UsesYahooRegularMarketPrice_DirectTwdRates()
    {
        // Each {CODE}TWD=X's meta.regularMarketPrice is used directly as "1 foreign = N TWD",
        // NO cross-rate division.
        var http = new HttpClient(new YahooStubHandler(YahooRates));
        AppSettings? saved = null;
        var svc = CreateCapturing(
            out var capture,
            rates: new Dictionary<string, decimal>(DefaultRates),
            http: http);
        capture.Saved += s => saved = s;
        bool fired = false;
        svc.CurrencyChanged += () => fired = true;

        await svc.RefreshRatesAsync();

        // Each rate is the pair's regularMarketPrice verbatim (no division).
        Assert.Equal(31.5m, svc.ExchangeRates["USD"]);
        Assert.Equal(0.196m, svc.ExchangeRates["JPY"]);
        Assert.Equal(36.72m, svc.ExchangeRates["EUR"]);
        Assert.Equal(4.05m, svc.ExchangeRates["HKD"]);
        // JPY magnitude sanity-check: ~0.2, NOT ~20 (Yahoo is per-1-unit, not per-100).
        Assert.InRange(svc.ExchangeRates["JPY"], 0.15m, 0.30m);
        Assert.True(fired, "CurrencyChanged should fire on a successful refresh");

        // Persisted with the new rates + a non-null refresh timestamp.
        Assert.NotNull(saved);
        Assert.Equal(31.5m, saved!.UsdTwdRate);
        Assert.Equal(31.5m, saved.ExchangeRates!["USD"]);
        Assert.Equal(0.196m, saved.ExchangeRates!["JPY"]);
        Assert.False(string.IsNullOrEmpty(saved.LastFxRefreshUtc));
    }

    [Fact]
    public async Task RefreshRatesAsync_RequestsYahooChartEndpointPerCurrency()
    {
        var handler = new YahooStubHandler(YahooRates);
        var http = new HttpClient(handler);
        var svc = Create("TWD", rates: new Dictionary<string, decimal>(DefaultRates), http: http);

        await svc.RefreshRatesAsync();

        // One Yahoo chart request per fetched currency, each the {CODE}TWD=X pair.
        Assert.Contains(handler.RequestedUrls, u => u.Contains("USDTWD=X", StringComparison.Ordinal));
        Assert.Contains(handler.RequestedUrls, u => u.Contains("JPYTWD=X", StringComparison.Ordinal));
        Assert.Contains(handler.RequestedUrls, u => u.Contains("EURTWD=X", StringComparison.Ordinal));
        Assert.Contains(handler.RequestedUrls, u => u.Contains("HKDTWD=X", StringComparison.Ordinal));
        Assert.All(handler.RequestedUrls, u =>
            Assert.StartsWith("https://query1.finance.yahoo.com/v8/finance/chart/", u));
    }

    [Fact]
    public async Task RefreshRatesAsync_Http500_DoesNotOverwrite()
    {
        // Every pair 500s → no fabricated write; rates stay as-is, no CurrencyChanged.
        var http = new HttpClient(new StatusHttpMessageHandler(HttpStatusCode.InternalServerError));
        AppSettings? saved = null;
        var svc = CreateCapturing(
            out var capture,
            rates: new Dictionary<string, decimal>(DefaultRates),
            http: http);
        capture.Saved += s => saved = s;
        bool fired = false;
        svc.CurrencyChanged += () => fired = true;

        await svc.RefreshRatesAsync();

        Assert.Equal(DefaultRates["USD"], svc.ExchangeRates["USD"]);
        Assert.Equal(DefaultRates["JPY"], svc.ExchangeRates["JPY"]);
        Assert.Equal(DefaultRates["EUR"], svc.ExchangeRates["EUR"]);
        Assert.Equal(DefaultRates["HKD"], svc.ExchangeRates["HKD"]);
        Assert.False(fired, "no refresh should be signalled when the fetch fails");
        Assert.Null(saved); // nothing persisted on failure
    }

    [Fact]
    public async Task RefreshRatesAsync_GarbageBody_DoesNotOverwrite()
    {
        // 200 OK but an HTML anti-bot page (not JSON) for every pair → rates unchanged.
        var http = new HttpClient(new BodyHttpMessageHandler(
            "<html><head><title>Challenge Validation</title></head><body>nope</body></html>"));
        var svc = Create("TWD", rates: new Dictionary<string, decimal>(DefaultRates), http: http);
        bool fired = false;
        svc.CurrencyChanged += () => fired = true;

        await svc.RefreshRatesAsync();

        Assert.Equal(DefaultRates["USD"], svc.ExchangeRates["USD"]);
        Assert.Equal(DefaultRates["JPY"], svc.ExchangeRates["JPY"]);
        Assert.False(fired, "no refresh should be signalled when USD can't be parsed");
    }

    [Fact]
    public async Task RefreshRatesAsync_WhenCurrencyMissingOrZero_KeepsThatRate()
    {
        // USD valid, JPY quote is 0 (invalid), HKD pair absent entirely → JPY & HKD keep prior values.
        var partial = new Dictionary<string, decimal>
        {
            ["USD"] = 31.5m,
            ["JPY"] = 0m,     // regularMarketPrice 0 → treated as no quote
            ["EUR"] = 36.72m,
            // HKD omitted → handler 404s that pair
        };
        var http = new HttpClient(new YahooStubHandler(partial));
        var svc = Create("TWD", rates: new Dictionary<string, decimal>(DefaultRates), http: http);

        await svc.RefreshRatesAsync();

        Assert.Equal(31.5m, svc.ExchangeRates["USD"]);
        Assert.Equal(36.72m, svc.ExchangeRates["EUR"]);
        Assert.Equal(DefaultRates["JPY"], svc.ExchangeRates["JPY"]); // 0 quote → keep prior
        Assert.Equal(DefaultRates["HKD"], svc.ExchangeRates["HKD"]); // absent pair → keep prior
    }

    [Fact]
    public async Task RefreshRatesAsync_RetriesTransientFailure_ThenSucceeds()
    {
        // 前兩輪整批失敗（模擬啟動初期網路/DNS 還沒就緒），第三輪成功 → 應重試到成功、匯率更新。
        var handler = new YahooTransientHandler(failSweeps: 2, rates: YahooRates);
        var http = new HttpClient(handler);
        AppSettings? saved = null;
        var svc = CreateCapturing(out var capture, rates: new Dictionary<string, decimal>(DefaultRates), http: http);
        capture.Saved += s => saved = s;
        bool fired = false;
        svc.CurrencyChanged += () => fired = true;

        await svc.RefreshRatesAsync();

        Assert.Equal(3, handler.Sweeps);                 // 1 失敗 + 2 失敗 + 3 成功
        Assert.Equal(31.5m, svc.ExchangeRates["USD"]);   // 重試後成功更新
        Assert.True(fired, "CurrencyChanged should fire once a retry succeeds");
        Assert.NotNull(saved);
    }

    [Fact]
    public async Task RefreshRatesAsync_AllAttemptsFail_KeepsCachedRates()
    {
        // 每輪都失敗（連 max 次都失敗）→ 不覆寫、退回快取/預設值、不觸發事件。
        var handler = new YahooTransientHandler(failSweeps: int.MaxValue, rates: YahooRates);
        var http = new HttpClient(handler);
        var svc = Create("TWD", rates: new Dictionary<string, decimal>(DefaultRates), http: http);
        bool fired = false;
        svc.CurrencyChanged += () => fired = true;

        await svc.RefreshRatesAsync();

        Assert.Equal(3, handler.Sweeps);                       // 重試到用完（預設 3 次）
        Assert.Equal(DefaultRates["USD"], svc.ExchangeRates["USD"]);
        Assert.False(fired);
    }

    // ── ParseYahooChartPrice (unit) ────────────────────────────────────────

    [Fact]
    public void ParseYahooChartPrice_ExtractsRegularMarketPrice()
    {
        Assert.Equal(31.555m, CurrencyService.ParseYahooChartPrice(YahooChartJson(31.555m)));
    }

    [Fact]
    public void ParseYahooChartPrice_MalformedJson_ReturnsNull()
    {
        // An HTML anti-bot page (the exact failure mode that killed the BOT source) → null.
        Assert.Null(CurrencyService.ParseYahooChartPrice(
            "<html><title>Challenge Validation</title></html>"));
    }

    [Fact]
    public void ParseYahooChartPrice_EmptyResult_ReturnsNull()
    {
        Assert.Null(CurrencyService.ParseYahooChartPrice("{\"chart\":{\"result\":[]}}"));
    }

    [Fact]
    public void ParseYahooChartPrice_MissingPrice_ReturnsNull()
    {
        Assert.Null(CurrencyService.ParseYahooChartPrice("{\"chart\":{\"result\":[{\"meta\":{}}]}}"));
    }

    [Fact]
    public void ParseYahooChartPrice_ZeroPrice_ReturnsNull()
    {
        // regularMarketPrice ≤ 0 is not a usable rate (would divide-by-zero in Convert).
        Assert.Null(CurrencyService.ParseYahooChartPrice(YahooChartJson(0m)));
    }

    /// <summary>
    /// Like <see cref="Create"/> but exposes a hook that fires whenever the
    /// service persists settings, so a test can capture the saved
    /// <see cref="AppSettings"/>.
    /// </summary>
    private static CurrencyService CreateCapturing(
        out SaveCapture capture,
        string currency = "TWD",
        decimal usdRate = 32.0m,
        Dictionary<string, decimal>? rates = null,
        HttpClient? http = null)
    {
        var cap = new SaveCapture();
        var settings = new Mock<IAppSettingsService>();
        settings.SetupGet(s => s.Current)
            .Returns(new AppSettings(
                PreferredCurrency: currency,
                UsdTwdRate: usdRate,
                ExchangeRates: rates));
        settings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>(), It.IsAny<bool>()))
            .Returns((AppSettings s, bool _) => { cap.Raise(s); return Task.CompletedTask; });
        http ??= new HttpClient();
        capture = cap;
        return new CurrencyService(settings.Object, http, NullLogger<CurrencyService>.Instance,
            fxRetryBaseDelay: TimeSpan.Zero); // 測試不等退避
    }

    private sealed class SaveCapture
    {
        public event Action<AppSettings>? Saved;
        public void Raise(AppSettings s) => Saved?.Invoke(s);
    }

    /// <summary>Returns the given status for every request (no body of interest).</summary>
    private sealed class StatusHttpMessageHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(string.Empty),
            });
    }

    /// <summary>Returns 200 OK with the same body for every request.</summary>
    private sealed class BodyHttpMessageHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
    }

    /// <summary>
    /// URL-aware Yahoo stub: serves <c>YahooChartJson(rates[code])</c> for a known
    /// <c>{CODE}TWD=X</c> pair, 404 for an unknown one. Records every requested URL.
    /// </summary>
    private sealed class YahooStubHandler(IReadOnlyDictionary<string, decimal> rates) : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            RequestedUrls.Add(url);
            var code = ExtractPairCode(url);
            if (code is not null && rates.TryGetValue(code, out var price))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(YahooChartJson(price)),
                });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}"),
            });
        }
    }

    /// <summary>
    /// Fails the first <paramref name="failSweeps"/> full sweeps, then succeeds. A "sweep"
    /// starts when the USD pair is requested (it is fetched first), letting the retry test
    /// count whole-batch attempts rather than raw HTTP calls.
    /// </summary>
    private sealed class YahooTransientHandler(int failSweeps, IReadOnlyDictionary<string, decimal> rates)
        : HttpMessageHandler
    {
        public int Sweeps { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            var code = ExtractPairCode(url);
            if (code == "USD")
                Sweeps++; // a new sweep begins with the first (USD) request

            if (Sweeps <= failSweeps)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent(string.Empty),
                });

            var price = code is not null && rates.TryGetValue(code, out var p) ? p : 0m;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(YahooChartJson(price)),
            });
        }
    }
}
