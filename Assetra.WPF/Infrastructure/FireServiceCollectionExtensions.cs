using Assetra.Application.Fire;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Core.Interfaces.Fire;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Infrastructure.Persistence;
using Assetra.WPF.Features.Fire;
using Assetra.WPF.Features.Portfolio;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class FireServiceCollectionExtensions
{
    public static IServiceCollection AddFireContext(this IServiceCollection services, string dbPath)
    {
        services.AddSingleton<IFireCalculatorService, FireCalculatorService>();
        services.AddSingleton<IFirePlanningService, FirePlanningService>();
        services.AddSingleton<IFireDrawdownService, FireDrawdownService>();
        services.AddSingleton<IFireMonteCarloService, FireMonteCarloService>();
        services.AddSingleton<FireScenarioSqliteRepository>(_ => new FireScenarioSqliteRepository(dbPath));
        services.AddSingleton<IFireScenarioRepository>(sp => sp.GetRequiredService<FireScenarioSqliteRepository>());
        services.AddSingleton<IAppNetWorthProvider>(sp => new AppNetWorthProvider(
            sp.GetRequiredService<IFinancialOverviewQueryService>(),
            sp.GetRequiredService<PortfolioViewModel>(),
            sp.GetRequiredService<IRealEstateValuationService>(),
            sp.GetRequiredService<IRetirementProjectionService>(),
            sp.GetRequiredService<IPhysicalAssetValuationService>()));
        services.AddSingleton<FireViewModel>();
        return services;
    }
}
