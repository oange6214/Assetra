using Assetra.Core.Models.MultiAsset;

namespace Assetra.Core.Interfaces.MultiAsset;

public interface IPhysicalAssetRepository
{
    Task<IReadOnlyList<PhysicalAsset>> GetAllAsync(CancellationToken ct = default);
    Task<PhysicalAsset?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(PhysicalAsset entity, CancellationToken ct = default);
    Task UpdateAsync(PhysicalAsset entity, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
