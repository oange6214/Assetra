using Assetra.Application.Reconciliation;
using Assetra.Core.DomainServices.Reconciliation;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Interfaces.Reconciliation;
using Assetra.Infrastructure.Import;
using Assetra.Infrastructure.Persistence;
using Assetra.WPF.Features.Reconciliation;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class ReconciliationServiceCollectionExtensions
{
    public static IServiceCollection AddReconciliationContext(this IServiceCollection services, string dbPath)
    {
        services.AddSingleton<IReconciliationMatcher>(_ => new DefaultReconciliationMatcher());
        services.AddSingleton<IReconciliationSessionRepository>(_ => new ReconciliationSessionSqliteRepository(dbPath));
        services.AddSingleton<IReconciliationService>(sp => new ReconciliationService(
            sp.GetRequiredService<IReconciliationSessionRepository>(),
            sp.GetRequiredService<ITradeRepository>(),
            sp.GetRequiredService<IReconciliationMatcher>(),
            sp.GetService<IImportRowApplier>()));
        services.AddSingleton<ReconciliationViewModel>(sp => new ReconciliationViewModel(
            sp.GetRequiredService<IReconciliationService>(),
            sp.GetRequiredService<IReconciliationSessionRepository>(),
            sp.GetRequiredService<IAssetRepository>(),
            sp.GetRequiredService<ITradeRepository>(),
            sp.GetRequiredService<IReconciliationMatcher>(),
            sp.GetService<IImportBatchHistoryRepository>(),
            sp.GetService<IImportFormatDetector>(),
            sp.GetService<ImportParserFactory>(),
            sp.GetService<ILocalizationService>(),
            sp.GetService<ICurrencyService>()));
        return services;
    }
}
