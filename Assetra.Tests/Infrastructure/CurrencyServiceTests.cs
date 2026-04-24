using Moq;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Http;
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
        HttpClient? http = null)
    {
        var settings = new Mock<IAppSettingsService>();
        settings.SetupGet(s => s.Current)
            .Returns(new AppSettings(
                PreferredCurrency: currency,
                UsdTwdRate: usdRate,
                ExchangeRates: rates));
        settings.Setup(s => s.SaveAsync(It.IsAny<AppSettings>())).Returns(Task.CompletedTask);
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
        Assert.Equal(4.1m,  svc.ExchangeRates["HKD"]);
    }

    [Fact]
    public void ExchangeRates_WhenPresentInSettings_UsesPersistedValues()
    {
        var persisted = new Dictionary<string, decimal>
        {
            ["USD"] = 33.5m, ["JPY"] = 0.22m, ["EUR"] = 36.0m, ["HKD"] = 4.3m,
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
        Assert.Equal("-$1,000", svc.FormatSigned(-32_000m));
    }

    [Fact]
    public void FormatSigned_Positive_USD_PrependsPlusBeforeCurrency()
    {
        var svc = Create("USD", rates: DefaultRates);
        Assert.Equal("+$1,000", svc.FormatSigned(32_000m));
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

    [Fact]
    public async Task RefreshRatesAsync_WhenResponseOmitsRates_KeepsExistingRates()
    {
        var http = CreateHttpClient("""{"amount":1.0,"base":"USD"}""");
        var svc = Create("TWD", rates: new Dictionary<string, decimal>(DefaultRates), http: http);

        await svc.RefreshRatesAsync();

        Assert.Equal(DefaultRates["USD"], svc.ExchangeRates["USD"]);
        Assert.Equal(DefaultRates["JPY"], svc.ExchangeRates["JPY"]);
        Assert.Equal(DefaultRates["EUR"], svc.ExchangeRates["EUR"]);
        Assert.Equal(DefaultRates["HKD"], svc.ExchangeRates["HKD"]);
    }

    [Fact]
    public async Task RefreshRatesAsync_WhenResponseOmitsOptionalRate_UpdatesAvailableRatesOnly()
    {
        var http = CreateHttpClient(
            """{"rates":{"TWD":31.5,"JPY":150.0,"EUR":0.92},"base":"USD"}""");
        var svc = Create("TWD", rates: new Dictionary<string, decimal>(DefaultRates), http: http);

        await svc.RefreshRatesAsync();

        Assert.Equal(31.5m, svc.ExchangeRates["USD"]);
        Assert.Equal(Math.Round(31.5m / 150.0m, 4), svc.ExchangeRates["JPY"]);
        Assert.Equal(Math.Round(31.5m / 0.92m, 4), svc.ExchangeRates["EUR"]);
        Assert.Equal(DefaultRates["HKD"], svc.ExchangeRates["HKD"]);
    }

    private static HttpClient CreateHttpClient(string responseBody) =>
        new(new StubHttpMessageHandler(responseBody))
        {
            BaseAddress = new Uri("https://api.frankfurter.app/")
        };

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
