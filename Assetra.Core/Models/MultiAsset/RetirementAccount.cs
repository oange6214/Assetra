using Assetra.Core.Interfaces.Sync;
using Assetra.Core.Models.Sync;

namespace Assetra.Core.Models.MultiAsset;

/// <summary>
/// 退休專戶：記錄勞退、自提、IRA、年金等退休準備帳戶。
/// </summary>
public sealed record RetirementAccount(
    Guid Id,
    string Name,
    RetirementAccountType AccountType,
    string Provider,
    decimal Balance,
    decimal EmployeeContributionRate,
    decimal EmployerContributionRate,
    int YearsOfService,
    int LegalWithdrawalAge,
    DateOnly OpenedDate,
    string Currency,
    RetirementAccountStatus Status,
    string? Notes,
    EntityVersion Version) : IVersionedEntity
{
    Guid IVersionedEntity.EntityId => Id;
}
