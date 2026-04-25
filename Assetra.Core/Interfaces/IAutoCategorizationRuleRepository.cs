using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IAutoCategorizationRuleRepository
{
    Task<IReadOnlyList<AutoCategorizationRule>> GetAllAsync(CancellationToken ct = default);
    Task<AutoCategorizationRule?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(AutoCategorizationRule rule, CancellationToken ct = default);
    Task UpdateAsync(AutoCategorizationRule rule, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
