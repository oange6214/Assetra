using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;

namespace Assetra.Application.MultiAsset;

/// <summary>
/// 實物資產估值服務：彙整活躍實物資產的當前市值與未實現損益。
/// </summary>
public sealed class PhysicalAssetValuationService : IPhysicalAssetValuationService
{
    private readonly IPhysicalAssetRepository _assets;

    public PhysicalAssetValuationService(IPhysicalAssetRepository assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        _assets = assets;
    }

    public async Task<decimal> GetTotalCurrentValueAsync(CancellationToken ct = default)
    {
        var all = await _assets.GetAllAsync(ct).ConfigureAwait(false);
        return all.Where(a => a.Status == PhysicalAssetStatus.Active).Sum(a => a.CurrentValue);
    }

    public async Task<decimal> GetTotalUnrealizedGainAsync(CancellationToken ct = default)
    {
        var all = await _assets.GetAllAsync(ct).ConfigureAwait(false);
        return all.Where(a => a.Status == PhysicalAssetStatus.Active).Sum(a => a.UnrealizedGain);
    }

    public async Task<IReadOnlyList<PhysicalAssetSummary>> GetSummariesAsync(
        CancellationToken ct = default)
    {
        var all = await _assets.GetAllAsync(ct).ConfigureAwait(false);
        var active = all.Where(a => a.Status == PhysicalAssetStatus.Active).ToList();

        var results = new List<PhysicalAssetSummary>(active.Count);
        foreach (var asset in active)
        {
            ct.ThrowIfCancellationRequested();
            var rate = asset.AcquisitionCost == 0m
                ? 0m
                : asset.UnrealizedGain / asset.AcquisitionCost;
            results.Add(new PhysicalAssetSummary(asset, asset.UnrealizedGain, rate));
        }
        return results;
    }
}
