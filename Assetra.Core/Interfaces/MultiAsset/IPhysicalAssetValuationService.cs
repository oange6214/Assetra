using Assetra.Core.Models.MultiAsset;

namespace Assetra.Core.Interfaces.MultiAsset;

public interface IPhysicalAssetValuationService
{
    /// <summary>
    /// 所有活躍實物資產的當前市值總和。
    /// </summary>
    Task<decimal> GetTotalCurrentValueAsync(CancellationToken ct = default);

    /// <summary>
    /// 所有活躍實物資產的未實現損益總和（CurrentValue − AcquisitionCost）。
    /// </summary>
    Task<decimal> GetTotalUnrealizedGainAsync(CancellationToken ct = default);

    /// <summary>
    /// 取得每個活躍實物資產的摘要。
    /// </summary>
    Task<IReadOnlyList<PhysicalAssetSummary>> GetSummariesAsync(CancellationToken ct = default);
}

public sealed record PhysicalAssetSummary(
    PhysicalAsset Asset,
    decimal UnrealizedGain,
    decimal UnrealizedGainRate);
