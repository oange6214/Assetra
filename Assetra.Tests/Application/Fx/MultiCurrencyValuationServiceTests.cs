using Assetra.Application.Fx;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Application.Fx;

public class MultiCurrencyValuationServiceTests
{
    [Fact]
    public async Task ConvertAsync_SameCurrency_ReturnsAmount()
    {
        var sut = new MultiCurrencyValuationService(new StubProvider(null));
        var r = await sut.ConvertAsync(123.45m, "TWD", "TWD", new DateOnly(2026, 4, 28));
        Assert.Equal(123.45m, r);
    }

    [Fact]
    public async Task ConvertAsync_RateAvailable_MultipliesAmount()
    {
        var sut = new MultiCurrencyValuationService(new StubProvider(32m));
        var r = await sut.ConvertAsync(10m, "USD", "TWD", new DateOnly(2026, 4, 28));
        Assert.Equal(320m, r);
    }

    [Fact]
    public async Task ConvertAsync_NoRate_ReturnsNull()
    {
        var sut = new MultiCurrencyValuationService(new StubProvider(null));
        var r = await sut.ConvertAsync(10m, "USD", "JPY", new DateOnly(2026, 4, 28));
        Assert.Null(r);
    }

    [Fact]
    public async Task ConvertAsync_BlankCurrency_ReturnsNull()
    {
        var sut = new MultiCurrencyValuationService(new StubProvider(32m));
        var r = await sut.ConvertAsync(10m, "", "TWD", new DateOnly(2026, 4, 28));
        Assert.Null(r);
    }

    private sealed class StubProvider : IFxRateProvider
    {
        private readonly decimal? _rate;
        public StubProvider(decimal? rate) { _rate = rate; }
        public Task<decimal?> GetRateAsync(string from, string to, DateOnly asOf, CancellationToken ct = default)
            => Task.FromResult(_rate);
        public Task<IReadOnlyList<FxRate>> GetHistoricalSeriesAsync(string from, string to, DateOnly start, DateOnly end, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<FxRate>>(Array.Empty<FxRate>());
    }
}
