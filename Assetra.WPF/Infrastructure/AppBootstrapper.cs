using Microsoft.Extensions.Hosting;
using Assetra.Infrastructure.Persistence;

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
            .AddAssetraDataServices(paths.DbPath)
            .AddAssetraApplicationServices()
            .AddAssetraViewModels()
            .AddAssetraHostedServices();

        var host = builder.Build();
        AppStartupTasks.ApplySavedUiPreferences(host.Services, savedSettings);
        AppStartupTasks.StartBackgroundWarmups(host.Services, paths.AssetsDir);

        return host;
    }
}
