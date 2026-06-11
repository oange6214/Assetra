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

    private static CurrencyService Create(
        string currency = "TWD",
        decimal usdRate = 32.0m,
        Dictionary<string, decimal>? rates = null,
        HttpClient? http = null,
        IFxRateHistoryFetcher? fetcher = null)
    {
        var settings = new Mock<IAppSettingsService>();
        settings.SetupGet(s => s.Current)
            .Returns(new AppSettings(
                PreferredCurrency: currency,
                UsdTwdRate: usdRate,
                ExchangeRates: rates));
        settings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);
        http ??= new HttpClient();
        // Default fetcher: empty Yahoo history (no USD→TWD). Tests that exercise
        // a successful refresh pass an explicit fetcher returning a rate.
        fetcher ??= FakeFetcher(Array.Empty<FxRateHistoryEntry>());
        return new CurrencyService(settings.Object, http, fetcher, NullLogger<CurrencyService>.Instance);
    }

    /// <summary>
    /// A stand-in <see cref="IFxRateHistoryFetcher"/> that returns a fixed list,
    /// mirroring the real Yahoo fetcher's "never throw, return what we have"
    /// contract.
    /// </summary>
    private static IFxRateHistoryFetcher FakeFetcher(IReadOnlyList<FxRateHistoryEntry> entries)
    {
        var mock = new Mock<IFxRateHistoryFetcher>();
        mock.SetupGet(f => f.SourceName).Returns("yahoo");
        mock.Setup(f => f.FetchAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);
        return mock.Object;
    }

    /// <summary>Single-day USDTWD history whose latest entry has the given rate.</summary>
    private static IFxRateHistoryFetcher YahooUsdTwd(decimal rate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        // Two entries; the MAX-Date one (today) carries the rate the service must pick.
        var entries = new[]
        {
            new FxRateHistoryEntry(today.AddDays(-1), "USD", "TWD", rate - 0.5m, "yahoo", DateTimeOffset.UtcNow),
            new FxRateHistoryEntry(today, "USD", "TWD", rate, "yahoo", DateTimeOffset.UtcNow),
        };
        return FakeFetcher(entries);
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

    // ── RefreshRatesAsync: Yahoo USD→TWD + Frankfurter cross-rates ─────────

    [Fact]
    public async Task RefreshRatesAsync_UsesYahooUsdTwd_AndFrankfurterCrossRates()
    {
        // Yahoo gives the live USD→TWD; Frankfurter gives USD→{EUR,HKD,JPY}.
        var http = CreateHttpClient(
            """{"rates":{"EUR":0.86678,"HKD":7.8365,"JPY":160.54},"base":"USD"}""");
        AppSettings? saved = null;
        var svc = CreateCapturing(
            out var capture,
            rates: new Dictionary<string, decimal>(DefaultRates),
            http: http,
            fetcher: YahooUsdTwd(31.65m));
        capture.Saved += s => saved = s;
        bool fired = false;
        svc.CurrencyChanged += () => fired = true;

        await svc.RefreshRatesAsync();

        // USD comes straight from Yahoo's latest (MAX-Date) entry.
        Assert.Equal(31.65m, svc.ExchangeRates["USD"]);
        // Cross-rates = usdToTwd / usdToForeign, rounded to 4dp (banker's rounding).
        Assert.Equal(Math.Round(31.65m / 0.86678m, 4), svc.ExchangeRates["EUR"]); // 36.5145
        Assert.Equal(Math.Round(31.65m / 160.54m, 4), svc.ExchangeRates["JPY"]);  // 0.1971
        Assert.Equal(Math.Round(31.65m / 7.8365m, 4), svc.ExchangeRates["HKD"]);  // 4.0388
        Assert.True(fired, "CurrencyChanged should fire on a successful refresh");

        // Persisted with the new rates + a non-null refresh timestamp.
        Assert.NotNull(saved);
        Assert.Equal(31.65m, saved!.UsdTwdRate);
        Assert.Equal(31.65m, saved.ExchangeRates!["USD"]);
        Assert.Equal(Math.Round(31.65m / 0.86678m, 4), saved.ExchangeRates!["EUR"]);
        Assert.False(string.IsNullOrEmpty(saved.LastFxRefreshUtc));
    }

    [Fact]
    public async Task RefreshRatesAsync_YahooEmpty_DoesNotOverwrite()
    {
        // Yahoo returns nothing → no fabricated TWD write; rates stay as-is.
        var http = CreateHttpClient(
            """{"rates":{"EUR":0.86678,"HKD":7.8365,"JPY":160.54},"base":"USD"}""");
        var svc = Create(
            "TWD",
            rates: new Dictionary<string, decimal>(DefaultRates),
            http: http,
            fetcher: FakeFetcher(Array.Empty<FxRateHistoryEntry>()));
        bool fired = false;
        svc.CurrencyChanged += () => fired = true;

        await svc.RefreshRatesAsync();

        Assert.Equal(DefaultRates["USD"], svc.ExchangeRates["USD"]);
        Assert.Equal(DefaultRates["JPY"], svc.ExchangeRates["JPY"]);
        Assert.Equal(DefaultRates["EUR"], svc.ExchangeRates["EUR"]);
        Assert.Equal(DefaultRates["HKD"], svc.ExchangeRates["HKD"]);
        Assert.False(fired, "no refresh should be signalled when USD→TWD is unavailable");
    }

    [Fact]
    public async Task RefreshRatesAsync_WhenFrankfurterOmitsOptionalRate_KeepsThatRate()
    {
        // Yahoo gives USD→TWD; Frankfurter omits HKD → HKD keeps its prior value.
        var http = CreateHttpClient(
            """{"rates":{"JPY":160.54,"EUR":0.86678},"base":"USD"}""");
        var svc = Create(
            "TWD",
            rates: new Dictionary<string, decimal>(DefaultRates),
            http: http,
            fetcher: YahooUsdTwd(31.65m));

        await svc.RefreshRatesAsync();

        Assert.Equal(31.65m, svc.ExchangeRates["USD"]);
        Assert.Equal(Math.Round(31.65m / 160.54m, 4), svc.ExchangeRates["JPY"]);
        Assert.Equal(Math.Round(31.65m / 0.86678m, 4), svc.ExchangeRates["EUR"]);
        Assert.Equal(DefaultRates["HKD"], svc.ExchangeRates["HKD"]);
    }

    [Fact]
    public async Task RefreshRatesAsync_WhenFrankfurterFails_StillAppliesYahooUsd()
    {
        // Frankfurter 500s → cross-rates unchanged, but USD still updates from Yahoo.
        var http = new HttpClient(new StatusHttpMessageHandler(System.Net.HttpStatusCode.InternalServerError))
        {
            BaseAddress = new Uri("https://api.frankfurter.app/"),
        };
        var svc = Create(
            "TWD",
            rates: new Dictionary<string, decimal>(DefaultRates),
            http: http,
            fetcher: YahooUsdTwd(31.65m));

        await svc.RefreshRatesAsync();

        Assert.Equal(31.65m, svc.ExchangeRates["USD"]);
        Assert.Equal(DefaultRates["JPY"], svc.ExchangeRates["JPY"]);
        Assert.Equal(DefaultRates["EUR"], svc.ExchangeRates["EUR"]);
        Assert.Equal(DefaultRates["HKD"], svc.ExchangeRates["HKD"]);
    }

    private static HttpClient CreateHttpClient(string responseBody) =>
        new(new StubHttpMessageHandler(responseBody))
        {
            BaseAddress = new Uri("https://api.frankfurter.app/")
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
        HttpClient? http = null,
        IFxRateHistoryFetcher? fetcher = null)
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
        fetcher ??= FakeFetcher(Array.Empty<FxRateHistoryEntry>());
        capture = cap;
        return new CurrencyService(settings.Object, http, fetcher, NullLogger<CurrencyService>.Instance);
    }

    private sealed class SaveCapture
    {
        public event Action<AppSettings>? Saved;
        public void Raise(AppSettings s) => Saved?.Invoke(s);
    }

    private sealed class StatusHttpMessageHandler(System.Net.HttpStatusCode status) : HttpMessageHandler
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
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody),
            });
    }
}
