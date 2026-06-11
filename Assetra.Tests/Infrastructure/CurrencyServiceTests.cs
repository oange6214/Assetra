using System.Net;
using System.Net.Http;
using System.Text;
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
    /// A realistic Bank of Taiwan daily CSV (/xrt/flcsv/0/day). First line is the
    /// header (with a leading BOM as the live endpoint serves). Each currency is
    /// one line; 即期買入 = col[3]. BOT quotes "1 foreign = N TWD" directly
    /// (USD≈31.5, JPY≈0.21, EUR≈36.5, HKD≈4.0 — NOT per-100).
    /// </summary>
    private const string BotDailyCsv =
        "﻿幣別,匯率,現金,即期,遠期10天,遠期30天,遠期60天,遠期90天,遠期120天,遠期150天,遠期180天,本行賣出,現金,即期,遠期10天,遠期30天,遠期60天,遠期90天,遠期120天,遠期150天,遠期180天\n" +
        "USD,本行買入,31.23000,31.55500,31.56700,0,0,0,0,0,0,本行賣出,31.90000,31.65500,0,0,0,0,0,0,0\n" +
        "JPY,本行買入,0.2050,0.2100,0,0,0,0,0,0,0,本行賣出,0.2180,0.2150,0,0,0,0,0,0,0\n" +
        "EUR,本行買入,35.80000,36.50000,0,0,0,0,0,0,0,本行賣出,37.50000,36.90000,0,0,0,0,0,0,0\n" +
        "HKD,本行買入,3.85000,4.03000,0,0,0,0,0,0,0,本行賣出,4.25000,4.12000,0,0,0,0,0,0,0\n";

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
        return new CurrencyService(settings.Object, http, NullLogger<CurrencyService>.Instance);
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

    // ── RefreshRatesAsync: Bank of Taiwan 即期買入 (spot-buy) ───────────────

    [Fact]
    public async Task RefreshRatesAsync_UsesBotSpotBuy_DirectTwdRates()
    {
        // BOT daily CSV → 即期買入 (col[3]) used directly as "1 foreign = N TWD",
        // NO cross-rate division.
        var http = CreateHttpClient(BotDailyCsv);
        AppSettings? saved = null;
        var svc = CreateCapturing(
            out var capture,
            rates: new Dictionary<string, decimal>(DefaultRates),
            http: http);
        capture.Saved += s => saved = s;
        bool fired = false;
        svc.CurrencyChanged += () => fired = true;

        await svc.RefreshRatesAsync();

        // Each rate is the CSV's 即期買入 column verbatim (no division).
        Assert.Equal(31.555m, svc.ExchangeRates["USD"]);
        Assert.Equal(0.2100m, svc.ExchangeRates["JPY"]);
        Assert.Equal(36.50000m, svc.ExchangeRates["EUR"]);
        Assert.Equal(4.03000m, svc.ExchangeRates["HKD"]);
        // JPY magnitude sanity-check: ~0.21, NOT ~21 (BOT is per-1-unit, not per-100).
        Assert.InRange(svc.ExchangeRates["JPY"], 0.15m, 0.30m);
        Assert.True(fired, "CurrencyChanged should fire on a successful refresh");

        // Persisted with the new rates + a non-null refresh timestamp.
        Assert.NotNull(saved);
        Assert.Equal(31.555m, saved!.UsdTwdRate);
        Assert.Equal(31.555m, saved.ExchangeRates!["USD"]);
        Assert.Equal(0.2100m, saved.ExchangeRates!["JPY"]);
        Assert.False(string.IsNullOrEmpty(saved.LastFxRefreshUtc));
    }

    [Fact]
    public async Task RefreshRatesAsync_RequestsBotDailyCsvEndpoint()
    {
        var handler = new RecordingHandler(BotDailyCsv);
        var http = new HttpClient(handler);
        var svc = Create("TWD", rates: new Dictionary<string, decimal>(DefaultRates), http: http);

        await svc.RefreshRatesAsync();

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(
            "https://rate.bot.com.tw/xrt/flcsv/0/day",
            handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task RefreshRatesAsync_Http500_DoesNotOverwrite()
    {
        // BOT 500s → no fabricated write; rates stay as-is, no CurrencyChanged.
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
        // 200 OK but not a parseable CSV (no USD line) → rates unchanged.
        var http = CreateHttpClient("this is not a CSV at all\nneither,is,this");
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
        // CSV has USD (valid) but JPY 即期買入 is 0 and omits HKD entirely → both keep prior values.
        const string partial =
            "﻿幣別,匯率,現金,即期,本行賣出,現金,即期\n" +
            "USD,本行買入,31.23000,31.55500,本行賣出,31.90000,31.65500\n" +
            "JPY,本行買入,0.2050,0,本行賣出,0.2180,0.2150\n" +
            "EUR,本行買入,35.80000,36.50000,本行賣出,37.50000,36.90000\n";
        var http = CreateHttpClient(partial);
        var svc = Create("TWD", rates: new Dictionary<string, decimal>(DefaultRates), http: http);

        await svc.RefreshRatesAsync();

        Assert.Equal(31.555m, svc.ExchangeRates["USD"]);
        Assert.Equal(36.50000m, svc.ExchangeRates["EUR"]);
        Assert.Equal(DefaultRates["JPY"], svc.ExchangeRates["JPY"]); // 0 in CSV → keep prior
        Assert.Equal(DefaultRates["HKD"], svc.ExchangeRates["HKD"]); // absent in CSV → keep prior
    }

    // ── ParseBotDailyCsv (unit) ────────────────────────────────────────────

    [Fact]
    public void ParseBotDailyCsv_TakesSpotBuyColumn_AndSkipsHeaderAndBom()
    {
        var parsed = CurrencyService.ParseBotDailyCsv(BotDailyCsv);

        Assert.Equal(31.555m, parsed["USD"]);
        Assert.Equal(0.2100m, parsed["JPY"]);
        Assert.Equal(36.50000m, parsed["EUR"]);
        Assert.Equal(4.03000m, parsed["HKD"]);
        Assert.DoesNotContain("幣別", parsed.Keys); // header row skipped
    }

    private static HttpClient CreateHttpClient(string responseBody) =>
        new(new StubHttpMessageHandler(responseBody))
        {
            BaseAddress = new Uri("https://rate.bot.com.tw/")
        };

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
        return new CurrencyService(settings.Object, http, NullLogger<CurrencyService>.Instance);
    }

    private sealed class SaveCapture
    {
        public event Action<AppSettings>? Saved;
        public void Raise(AppSettings s) => Saved?.Invoke(s);
    }

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

    private sealed class StubHttpMessageHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                // Serve UTF-8 bytes so the service's byte→UTF8 decode path is exercised.
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(responseBody)),
            });
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(responseBody)),
            });
        }
    }
}
