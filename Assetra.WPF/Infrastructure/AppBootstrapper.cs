using Assetra.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;

namespace Assetra.WPF.Infrastructure;

internal static class AppBootstrapper
{
    public static IHost Build()
    {
        var paths = AppRuntimePaths.Resolve();
        AppLogging.Configure(paths.LogDir);

        // Load persisted settings early so history provider is available
        var savedSettings = AppSettingsService.LoadSettings();

        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
        });

        builder.Services
            .AddAssetraPlatformServices(paths.AssetsDir, paths.DbPath)
            .AddPortfolioContext(paths.DbPath)
            .AddBudgetContext(paths.DbPath)
            .AddRecurringContext(paths.DbPath)
            .AddReportsContext()
            .AddGoalsContext(paths.DbPath)
            .AddAlertsContext(paths.DbPath)
            .AddLoansContext(paths.DbPath)
            .AddImportContext(paths.DbPath)
            .AddReconciliationContext(paths.DbPath)
            .AddAssetraShell()
            .AddAssetraHostedServices();

        var host = builder.Build();
        AppStartupTasks.ApplySavedUiPreferences(host.Services, savedSettings);
        AppStartupTasks.StartBackgroundWarmups(host.Services, paths.AssetsDir);

        return host;
    }
}
