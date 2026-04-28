using Assetra.Core.Models.MultiAsset;

namespace Assetra.Core.Interfaces.MultiAsset;

public interface IRetirementAccountRepository
{
    Task<IReadOnlyList<RetirementAccount>> GetAllAsync(CancellationToken ct = default);
    Task<RetirementAccount?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(RetirementAccount entity, CancellationToken ct = default);
    Task UpdateAsync(RetirementAccount entity, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
