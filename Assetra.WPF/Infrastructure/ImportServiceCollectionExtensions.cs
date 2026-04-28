using Assetra.Application.Import;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Import;
using Assetra.Infrastructure.Import;
using Assetra.Infrastructure.Import.Pdf;
using Assetra.Infrastructure.Persistence;
using Assetra.WPF.Features.Import;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class ImportServiceCollectionExtensions
{
    public static IServiceCollection AddImportContext(this IServiceCollection services, string dbPath)
    {
        services.AddSingleton<IImportFormatDetector, ImportFormatDetector>();
        services.AddSingleton<IPdfStatementParser, PdfPigStatementParser>();
        services.AddSingleton<Func<IOcrAdapter?>>(sp => () =>
        {
            var settings = sp.GetService<IAppSettingsService>()?.Current;
            if (settings is null)
                return null;
            var path = settings.OcrTessdataPath;
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
                return null;
            var lang = string.IsNullOrWhiteSpace(settings.OcrLanguage) ? "eng" : settings.OcrLanguage;
            try
            {
                return new TesseractOcrAdapter(path, lang);
            }
            catch
            {
                return null;
            }
        });
        services.AddSingleton<ImportParserFactory>(sp =>
            new ImportParserFactory(
                sp.GetService<IPdfStatementParser>(),
                sp.GetRequiredService<Func<IOcrAdapter?>>()));
        services.AddSingleton<IImportConflictDetector, ImportConflictDetector>();
        services.AddSingleton<IImportRowMapper, ImportRowMapper>();
        services.AddSingleton<IImportRowApplier>(sp => new DefaultImportRowApplier(
            sp.GetRequiredService<ITradeRepository>(),
            sp.GetRequiredService<IImportRowMapper>(),
            sp.GetService<IAutoCategorizationRuleRepository>()));
        services.AddSingleton<IImportBatchHistoryRepository>(_ => new ImportBatchHistorySqliteRepository(dbPath));
        services.AddSingleton<IImportApplyService>(sp => new ImportApplyService(
            sp.GetRequiredService<ITradeRepository>(),
            sp.GetRequiredService<IImportRowMapper>(),
            sp.GetService<IImportBatchHistoryRepository>(),
            sp.GetService<IAutoCategorizationRuleRepository>()));
        services.AddSingleton<IImportRollbackService, ImportRollbackService>();
        services.AddSingleton<ImportViewModel>();
        return services;
    }
}
