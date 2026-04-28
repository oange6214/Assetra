using Assetra.Application.MultiAsset;
using Assetra.Core.Interfaces.MultiAsset;
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

    private sealed class StubRepo(IReadOnlyList<PhysicalAsset> data) : IPhysicalAssetRepository
    {
        public Task<IReadOnlyList<PhysicalAsset>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(data);
        public Task<PhysicalAsset?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(data.FirstOrDefault(x => x.Id == id));
        public Task AddAsync(PhysicalAsset entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(PhysicalAsset entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }
}
