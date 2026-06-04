using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Infrastructure.Persistence;
using Assetra.WPF.Features.Goals;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class GoalsServiceCollectionExtensions
{
    public static IServiceCollection AddGoalsContext(
        this IServiceCollection services,
        string dbPath)
    {
        // Single concrete instance exposed as both the user-facing repo + sync store.
        services.AddSingleton<GoalSqliteRepository>(_ => new GoalSqliteRepository(dbPath));
        services.AddSingleton<IFinancialGoalRepository>(sp => sp.GetRequiredService<GoalSqliteRepository>());
        services.AddSingleton<IFinancialGoalSyncStore>(sp => sp.GetRequiredService<GoalSqliteRepository>());
        services.AddSingleton<GoalMilestoneSqliteRepository>(_ => new GoalMilestoneSqliteRepository(dbPath));
        services.AddSingleton<IGoalMilestoneRepository>(sp => sp.GetRequiredService<GoalMilestoneSqliteRepository>());
        services.AddSingleton<GoalFundingRuleSqliteRepository>(_ => new GoalFundingRuleSqliteRepository(dbPath));
        services.AddSingleton<IGoalFundingRuleRepository>(sp => sp.GetRequiredService<GoalFundingRuleSqliteRepository>());
        services.AddSingleton<IGoalProgressAmountProvider, GoalProgressAmountProvider>();
        services.AddSingleton<GoalsViewModel>();
        return services;
    }
}
