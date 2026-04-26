using System.Net.Http;
using System.Reactive.Concurrency;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Core.Interfaces;
using Assetra.Infrastructure;
using Assetra.Infrastructure.FinMind;
using Assetra.Infrastructure.History;
using Assetra.Infrastructure.Http;
using Assetra.Infrastructure.Persistence;
using Assetra.Infrastructure.Scheduling;
using Assetra.Infrastructure.Search;
using Assetra.WPF.Features.Settings;
using Assetra.WPF.Features.Snackbar;
using Assetra.WPF.Features.StatusBar;
using Assetra.WPF.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Assetra.WPF.Infrastructure;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAssetraPlatformServices(
        this IServiceCollection services,
        string assetsDir,
        string dbPath)
    {
        services.AddLogging(b => b.AddSerilog(dispose: true));
        services.AddSingleton<ILocalizationService, WpfLocalizationService>();
        services.AddSingleton<IThemeService, AppThemeService>();

        services.AddSingleton<HttpClient>(_ =>
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124.0 Safari/537.36");
            client.Timeout = TimeSpan.FromSeconds(20);
            return client;
        });
        services.AddSingleton<ITwseClient, TwseClient>();
        services.AddSingleton<ITpexClient, TpexClient>();
        services.AddSingleton<FugleClient>();
        services.AddSingleton<IStockSearchService>(_ => new StockSearchService(assetsDir));
        services.AddSingleton<IScheduler>(_ =>
            new DispatcherScheduler(System.Windows.Application.Current.Dispatcher));
        services.AddSingleton<IStockService>(sp => new StockScheduler(
            sp.GetRequiredService<ITwseClient>(),
            sp.GetRequiredService<ITpexClient>(),
            sp.GetRequiredService<IPortfolioRepository>(),
            sp.GetRequiredService<IAppSettingsService>(),
            sp.GetRequiredService<FugleClient>(),
            sp.GetRequiredService<IScheduler>()));

        services.AddSingleton<IAppSettingsService>(sp =>
            new AppSettingsService(sp.GetRequiredService<ILogger<AppSettingsService>>()));
        services.AddSingleton<ICurrencyService>(sp =>
            new CurrencyService(
                sp.GetRequiredService<IAppSettingsService>(),
                sp.GetRequiredService<HttpClient>(),
                sp.GetRequiredService<ILogger<CurrencyService>>()));

        services.AddSingleton<FinMindApiStatus>();
        services.AddSingleton<FinMindService>(sp =>
            new FinMindService(
                sp.GetRequiredService<HttpClient>(),
                token: string.Empty,
                sp.GetRequiredService<FinMindApiStatus>(),
                sp.GetRequiredService<ILogger<FinMindService>>()));
        services.AddSingleton<IFinMindService>(sp => sp.GetRequiredService<FinMindService>());
        services.AddSingleton<IStockHistoryProvider>(sp =>
        {
            var settingsSvc = sp.GetRequiredService<IAppSettingsService>();
            var http = sp.GetRequiredService<HttpClient>();
            var finMindService = sp.GetRequiredService<FinMindService>();
            var finMindStatus = sp.GetRequiredService<FinMindApiStatus>();

            var envOverride = Environment.GetEnvironmentVariable("STOCK_HISTORY_PROVIDER");
            if (!string.IsNullOrWhiteSpace(envOverride))
            {
                _ = settingsSvc.SaveAsync(settingsSvc.Current with { HistoryProvider = envOverride });
            }

            return new DynamicHistoryProvider(
                http,
                settingsSvc,
                finMindService,
                finMindStatus,
                sp.GetRequiredService<FugleClient>());
        });

        services.AddSingleton<SnackbarViewModel>();
        services.AddSingleton<ISnackbarService>(sp =>
            new SnackbarService(sp.GetRequiredService<SnackbarViewModel>()));

        services.AddSingleton<IStockraImportService>(_ => new StockraImportService(dbPath));
        return services;
    }

    public static IServiceCollection AddAssetraShell(this IServiceCollection services)
    {
        services.AddSingleton<NavRailViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<StatusBarViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainWindow>(sp =>
            new MainWindow(sp.GetRequiredService<MainViewModel>()));
        return services;
    }

    public static IServiceCollection AddAssetraHostedServices(this IServiceCollection services)
    {
        services.AddHostedService<DbInitializerService>();
        services.AddHostedService<MarketDataHostedService>();
        return services;
    }
}
