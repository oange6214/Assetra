using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IFinancialGoalRepository
{
    Task<IReadOnlyList<FinancialGoal>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(FinancialGoal goal, CancellationToken ct = default);
    Task UpdateAsync(FinancialGoal goal, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
