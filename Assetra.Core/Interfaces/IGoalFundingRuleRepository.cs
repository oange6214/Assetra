using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IGoalFundingRuleRepository
{
    Task<IReadOnlyList<GoalFundingRule>> GetByGoalAsync(Guid goalId, CancellationToken ct = default);
    Task<IReadOnlyList<GoalFundingRule>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(GoalFundingRule rule, CancellationToken ct = default);
    Task UpdateAsync(GoalFundingRule rule, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
