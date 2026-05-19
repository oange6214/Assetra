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
            .AddAnalysisContext()
            .AddFxContext(paths.DbPath)
            .AddGoalsContext(paths.DbPath)
            // Portfolio-Groups-Refactor P1：暴露 IPortfolioGroupRepository。Schema 在
            // constructor 內 idempotent migrate（建 table + seed default group），順序
            // 不重要——其他 schema migrator 內的 portfolio_group_id ALTER 是獨立 statement，
            // 不會被 portfolio_group table 是否存在影響。
            .AddPortfolioGroupsContext(paths.DbPath)
            .AddAlertsContext(paths.DbPath)
            .AddLoansContext(paths.DbPath)
            .AddImportContext(paths.DbPath)
            .AddReconciliationContext(paths.DbPath)
            .AddMultiAssetContext(paths.DbPath)
            .AddFireContext()
            .AddMonteCarloContext()
            .AddAssistantContext(paths.DbPath)
            .AddAssetraSync(paths.DataDir)
            .AddAssetraShell()
            .AddAssetraHostedServices();

        var host = builder.Build();
        AppStartupTasks.ApplySavedUiPreferences(host.Services, savedSettings);
        AppStartupTasks.StartBackgroundWarmups(host.Services, paths.AssetsDir);

        return host;
    }
}
