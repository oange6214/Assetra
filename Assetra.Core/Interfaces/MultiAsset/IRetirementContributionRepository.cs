using Assetra.Core.Models.MultiAsset;

namespace Assetra.Core.Interfaces.MultiAsset;

public interface IRetirementContributionRepository
{
    Task<IReadOnlyList<RetirementContribution>> GetByAccountAsync(Guid accountId, CancellationToken ct = default);
    Task<IReadOnlyList<RetirementContribution>> GetByYearAsync(int year, CancellationToken ct = default);
    Task AddAsync(RetirementContribution record, CancellationToken ct = default);
    Task UpdateAsync(RetirementContribution record, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
