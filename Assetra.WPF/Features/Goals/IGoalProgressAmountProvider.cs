using Assetra.Core.Models;

namespace Assetra.WPF.Features.Goals;

public interface IGoalProgressAmountProvider
{
    Task<decimal?> GetCurrentAmountAsync(FinancialGoal goal, CancellationToken ct = default);
}

public sealed class GoalProgressAmountProvider : IGoalProgressAmountProvider
{
    public Task<decimal?> GetCurrentAmountAsync(FinancialGoal goal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(goal);
        return Task.FromResult<decimal?>(goal.CurrentAmount);
    }
}
