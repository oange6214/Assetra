using Assetra.Core.Models.MultiAsset;

namespace Assetra.Core.Interfaces.MultiAsset;

public interface IInsurancePremiumRecordRepository
{
    Task<IReadOnlyList<InsurancePremiumRecord>> GetByPolicyAsync(Guid policyId, CancellationToken ct = default);
    Task<IReadOnlyList<InsurancePremiumRecord>> GetByPeriodAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task AddAsync(InsurancePremiumRecord record, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
