using Assetra.Application.Import;
using Assetra.Core.Interfaces.Import;
using Assetra.Infrastructure.Import;
using Assetra.Infrastructure.Persistence;
using Assetra.WPF.Features.Import;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class ImportServiceCollectionExtensions
{
    public static IServiceCollection AddImportContext(this IServiceCollection services, string dbPath)
    {
        services.AddSingleton<IImportFormatDetector, ImportFormatDetector>();
        services.AddSingleton<ImportParserFactory>();
        services.AddSingleton<IImportConflictDetector, ImportConflictDetector>();
        services.AddSingleton<IImportRuleRepository>(_ => new ImportRuleSqliteRepository(dbPath));
        services.AddSingleton<IImportRuleEngine, ImportRuleEngine>();
        services.AddSingleton<IImportRowMapper>(sp =>
            new ImportRowMapper(sp.GetService<IImportRuleEngine>()));
        services.AddSingleton<IImportBatchHistoryRepository>(_ => new ImportBatchHistorySqliteRepository(dbPath));
        services.AddSingleton<IImportApplyService, ImportApplyService>();
        services.AddSingleton<IImportRollbackService, ImportRollbackService>();
        services.AddSingleton<ImportViewModel>();
        return services;
    }
}
