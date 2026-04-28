using Assetra.Application.Fx;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class FxServiceCollectionExtensions
{
    public static IServiceCollection AddFxContext(
        this IServiceCollection services,
        string dbPath)
    {
        services.AddSingleton<IFxRateRepository>(_ => new FxRateSqliteRepository(dbPath));
        services.AddSingleton<IFxRateProvider, StaticFxRateProvider>();
        services.AddSingleton<IMultiCurrencyValuationService, MultiCurrencyValuationService>();
        return services;
    }
}
