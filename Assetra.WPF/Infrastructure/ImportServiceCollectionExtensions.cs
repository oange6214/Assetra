using Assetra.Application.Import;
using Assetra.Core.Interfaces.Import;
using Assetra.Infrastructure.Import;
using Assetra.WPF.Features.Import;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class ImportServiceCollectionExtensions
{
    public static IServiceCollection AddImportContext(this IServiceCollection services)
    {
        services.AddSingleton<IImportFormatDetector, ImportFormatDetector>();
        services.AddSingleton<ImportParserFactory>();
        services.AddSingleton<IImportConflictDetector, ImportConflictDetector>();
        services.AddSingleton<IImportApplyService, ImportApplyService>();
        services.AddSingleton<ImportViewModel>();
        return services;
    }
}
