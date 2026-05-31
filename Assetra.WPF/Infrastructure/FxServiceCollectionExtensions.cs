using Assetra.Application.Fx;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Infrastructure.Fx;
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

        // P4.1e — hybrid provider tries fx_rate_history (P4.1 store) first, falls
        // back to the legacy StaticFxRateProvider (fx_rate table). Existing
        // callers inject IFxRateProvider and transparently get historical rates
        // when available.
        services.AddSingleton<StaticFxRateProvider>();
        services.AddSingleton<IFxRateProvider>(sp =>
            new HybridFxRateProvider(
                history: sp.GetRequiredService<IFxRateHistoryService>(),
                legacy: sp.GetRequiredService<StaticFxRateProvider>()));

        services.AddSingleton<IMultiCurrencyValuationService, MultiCurrencyValuationService>();
        return services;
    }
}
