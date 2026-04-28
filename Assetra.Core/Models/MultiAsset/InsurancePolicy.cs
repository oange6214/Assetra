using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;

namespace Assetra.Core.Models.MultiAsset;

/// <summary>
/// 保險保單：記錄壽險、儲蓄險、年金等保單的基本資訊與現值。
/// </summary>
public sealed record InsurancePolicy(
    Guid Id,
    string Name,
    string PolicyNumber,
    InsuranceType Type,
    string Insurer,
    DateOnly StartDate,
    DateOnly? MaturityDate,
    decimal FaceValue,
    decimal CurrentCashValue,
    decimal AnnualPremium,
    string Currency,
    InsurancePolicyStatus Status,
    string? Notes,
    EntityVersion Version) : IVersionedEntity
{
    Guid IVersionedEntity.EntityId => Id;
}
