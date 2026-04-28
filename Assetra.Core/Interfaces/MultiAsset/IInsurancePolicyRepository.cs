using Assetra.Core.Models.MultiAsset;

namespace Assetra.Core.Interfaces.MultiAsset;

public interface IInsurancePolicyRepository
{
    Task<IReadOnlyList<InsurancePolicy>> GetAllAsync(CancellationToken ct = default);
    Task<InsurancePolicy?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(InsurancePolicy policy, CancellationToken ct = default);
    Task UpdateAsync(InsurancePolicy policy, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
