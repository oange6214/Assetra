using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;

namespace Assetra.Core.Models.MultiAsset;

/// <summary>
/// 不動產資產：記錄持有中或已出售的房地產。
/// </summary>
public sealed record RealEstate(
    Guid Id,
    string Name,
    string Address,
    decimal PurchasePrice,
    DateOnly PurchaseDate,
    decimal CurrentValue,
    decimal MortgageBalance,
    string Currency,
    bool IsRental,
    RealEstateStatus Status,
    string? Notes,
    EntityVersion Version) : IVersionedEntity
{
    Guid IVersionedEntity.EntityId => Id;

    public decimal Equity => CurrentValue - MortgageBalance;
}
