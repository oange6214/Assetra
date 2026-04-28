using Assetra.Application.Fx;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Application.Fx;

public class StaticFxRateProviderTests
{
    [Fact]
    public async Task GetRateAsync_SameCurrency_ReturnsOne()
    {
        var repo = new FakeFxRepo();
        var sut = new StaticFxRateProvider(repo);
        var r = await sut.GetRateAsync("TWD", "TWD", new DateOnly(2026, 4, 28));
        Assert.Equal(1m, r);
    }

    [Fact]
    public async Task GetRateAsync_DirectMatch_ReturnsRate()
    {
        var repo = new FakeFxRepo();
        repo.Rates.Add(new FxRate("USD", "TWD", 32.5m, new DateOnly(2026, 4, 28)));
        var sut = new StaticFxRateProvider(repo);
        var r = await sut.GetRateAsync("USD", "TWD", new DateOnly(2026, 4, 28));
        Assert.Equal(32.5m, r);
    }

    [Fact]
    public async Task GetRateAsync_InverseFallback_ReturnsReciprocal()
    {
        var repo = new FakeFxRepo();
        repo.Rates.Add(new FxRate("USD", "TWD", 32m, new DateOnly(2026, 4, 28)));
        var sut = new StaticFxRateProvider(repo);
        var r = await sut.GetRateAsync("TWD", "USD", new DateOnly(2026, 4, 28));
        Assert.NotNull(r);
        Assert.Equal(1m / 32m, r!.Value);
    }

    [Fact]
    public async Task GetRateAsync_NoData_ReturnsNull()
    {
        var sut = new StaticFxRateProvider(new FakeFxRepo());
        var r = await sut.GetRateAsync("USD", "JPY", new DateOnly(2026, 4, 28));
        Assert.Null(r);
    }

    private sealed class FakeFxRepo : IFxRateRepository
    {
        public List<FxRate> Rates { get; } = new();
        public Task UpsertAsync(FxRate rate, CancellationToken ct = default) { Rates.Add(rate); return Task.CompletedTask; }
        public Task UpsertManyAsync(IReadOnlyList<FxRate> rates, CancellationToken ct = default) { Rates.AddRange(rates); return Task.CompletedTask; }
        public Task<FxRate?> GetAsync(string from, string to, DateOnly asOf, CancellationToken ct = default)
        {
            var hit = Rates
                .Where(r => string.Equals(r.From, from, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(r.To, to, StringComparison.OrdinalIgnoreCase)
                         && r.AsOfDate <= asOf)
                .OrderByDescending(r => r.AsOfDate)
                .FirstOrDefault();
            return Task.FromResult<FxRate?>(hit);
        }
        public Task<IReadOnlyList<FxRate>> GetRangeAsync(string from, string to, DateOnly start, DateOnly end, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<FxRate>>(Rates
                .Where(r => string.Equals(r.From, from, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(r.To, to, StringComparison.OrdinalIgnoreCase)
                         && r.AsOfDate >= start && r.AsOfDate <= end)
                .OrderBy(r => r.AsOfDate).ToList());
    }
}
