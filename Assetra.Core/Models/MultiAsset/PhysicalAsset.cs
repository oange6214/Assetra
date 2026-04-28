using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;

namespace Assetra.Core.Models.MultiAsset;

/// <summary>
/// 實物資產：記錄車輛、珠寶、藝術品、收藏品、貴金屬等實物資產。
/// </summary>
public sealed record PhysicalAsset(
    Guid Id,
    string Name,
    PhysicalAssetCategory Category,
    string Description,
    decimal AcquisitionCost,
    DateOnly AcquisitionDate,
    decimal CurrentValue,
    string ValuationMethod,
    string Currency,
    PhysicalAssetStatus Status,
    string? Notes,
    EntityVersion Version) : IVersionedEntity
{
    Guid IVersionedEntity.EntityId => Id;

    public decimal UnrealizedGain => CurrentValue - AcquisitionCost;
}
