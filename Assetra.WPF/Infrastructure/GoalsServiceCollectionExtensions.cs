using Assetra.Core.Interfaces;
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
        services.AddSingleton<IFinancialGoalRepository>(_ => new GoalSqliteRepository(dbPath));
        services.AddSingleton<GoalsViewModel>();
        return services;
    }
}
