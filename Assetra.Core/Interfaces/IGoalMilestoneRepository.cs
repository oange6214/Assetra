using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IGoalMilestoneRepository
{
    Task<IReadOnlyList<GoalMilestone>> GetByGoalAsync(Guid goalId, CancellationToken ct = default);
    Task AddAsync(GoalMilestone milestone, CancellationToken ct = default);
    Task UpdateAsync(GoalMilestone milestone, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
