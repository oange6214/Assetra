using Assetra.Application.MultiAsset;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;
using Xunit;

namespace Assetra.Tests.Application.MultiAsset;

public class PhysicalAssetValuationServiceTests
{
    private static PhysicalAsset MakeAsset(
        string name,
        decimal cost,
        decimal currentValue,
        PhysicalAssetStatus status = PhysicalAssetStatus.Active) =>
        new(Guid.NewGuid(), name, PhysicalAssetCategory.Vehicle, "desc",
            cost, new DateOnly(2020, 1, 1), currentValue, "Market",
            "TWD", status, null, new EntityVersion());

    [Fact]
    public async Task GetTotalCurrentValue_ActiveOnly()
    {
        var active = MakeAsset("Car", 1_000_000m, 800_000m);
        var sold = MakeAsset("Old", 500_000m, 400_000m, PhysicalAssetStatus.Sold);
        var svc = new PhysicalAssetValuationService(new StubRepo([active, sold]));

        Assert.Equal(800_000m, await svc.GetTotalCurrentValueAsync());
    }

    [Fact]
    public async Task GetTotalUnrealizedGain_SumsActive()
    {
        var a = MakeAsset("Watch", 100_000m, 150_000m);
        var b = MakeAsset("Painting", 200_000m, 180_000m);
        var svc = new PhysicalAssetValuationService(new StubRepo([a, b]));

        Assert.Equal(30_000m, await svc.GetTotalUnrealizedGainAsync());
    }

    [Fact]
    public async Task GetSummaries_ComputesGainRate_ZeroCostSafe()
    {
        var a = MakeAsset("Gift", 0m, 100_000m);
        var b = MakeAsset("Watch", 100_000m, 150_000m);
        var svc = new PhysicalAssetValuationService(new StubRepo([a, b]));

        var summaries = await svc.GetSummariesAsync();

        Assert.Equal(2, summaries.Count);
        Assert.Equal(0m, summaries.First(s => s.Asset.Name == "Gift").UnrealizedGainRate);
        Assert.Equal(0.5m, summaries.First(s => s.Asset.Name == "Watch").UnrealizedGainRate);
    }

    [Fact]
    public async Task Totals_WithFxAndBaseCurrency_ConvertBeforeSumming()
    {
        var twd = MakeAsset("TWD", 80_000m, 100_000m);
        var usd = MakeAsset("USD", 1_000m, 1_500m) with { Currency = "USD" };
        var svc = new PhysicalAssetValuationService(
            new StubRepo([twd, usd]),
            new StubFx(("USD", "TWD"), 32m),
            new StubSettings(new AppSettings(BaseCurrency: "TWD")));

        var totalValue = await svc.GetTotalCurrentValueAsync();
        var totalGain = await svc.GetTotalUnrealizedGainAsync();
        var summaries = await svc.GetSummariesAsync();

        Assert.Equal(148_000m, totalValue);
        Assert.Equal(36_000m, totalGain);
        Assert.Equal("USD", summaries.Single(s => s.Asset.Id == usd.Id).Asset.Currency);
    }

    private sealed class StubRepo(IReadOnlyList<PhysicalAsset> data) : IPhysicalAssetRepository
    {
        public Task<IReadOnlyList<PhysicalAsset>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(data);
        public Task<PhysicalAsset?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(data.FirstOrDefault(x => x.Id == id));
        public Task AddAsync(PhysicalAsset entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(PhysicalAsset entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubFx((string From, string To) pair, decimal rate) : IMultiCurrencyValuationService
    {
        public Task<decimal?> ConvertAsync(decimal amount, string from, string to, DateOnly asOf, CancellationToken ct = default)
            => Task.FromResult<decimal?>(
                string.Equals(from, pair.From, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(to, pair.To, StringComparison.OrdinalIgnoreCase)
                    ? amount * rate
                    : amount);
    }

    private sealed class StubSettings(AppSettings current) : IAppSettingsService
    {
        public AppSettings Current { get; } = current;
        public event Action? Changed
        {
            add { }
            remove { }
        }
        public Task SaveAsync(AppSettings settings) => Task.CompletedTask;
    }
}
