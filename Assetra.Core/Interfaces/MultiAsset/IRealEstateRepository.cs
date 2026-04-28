using Assetra.Core.Models.MultiAsset;

namespace Assetra.Core.Interfaces.MultiAsset;

public interface IRealEstateRepository
{
    Task<IReadOnlyList<RealEstate>> GetAllAsync(CancellationToken ct = default);
    Task<RealEstate?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(RealEstate entity, CancellationToken ct = default);
    Task UpdateAsync(RealEstate entity, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
