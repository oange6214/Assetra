using System.IO;
using Microsoft.Extensions.Hosting;
using Serilog;
using Assetra.Infrastructure.Persistence;

namespace Assetra.WPF.Infrastructure;

internal static class AppBootstrapper
{
    public static IHost Build()
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Assetra", "logs");
        Directory.CreateDirectory(logDir);

        Log.Logger = new LoggerConfiguration()
            .WriteTo.File(Path.Combine(logDir, "app-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();

        var assetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");

        // Load persisted settings early so history provider is available
        var savedSettings = AppSettingsService.LoadSettings();

        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
        });

        // SQLite — all repositories share the same DB file
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Assetra");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "assetra.db");

        builder.Services
            .AddAssetraPlatformServices(assetsDir, dbPath)
            .AddAssetraDataServices(dbPath)
            .AddAssetraApplicationServices()
            .AddAssetraViewModels()
            .AddAssetraHostedServices();

        var host = builder.Build();
        AppStartupTasks.ApplySavedUiPreferences(host.Services, savedSettings);
        AppStartupTasks.StartBackgroundWarmups(host.Services, assetsDir);

        return host;
    }
}
