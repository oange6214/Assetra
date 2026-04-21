using System.IO;
using System.Net.Http;
using System.Reactive.Concurrency;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Assetra.AppLayer.Portfolio.Contracts;
using Assetra.AppLayer.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Infrastructure;
using Assetra.Infrastructure.Chart;
using Assetra.Infrastructure.FinMind;
using Assetra.Infrastructure.History;
using Assetra.Infrastructure.Http;
using Assetra.Infrastructure.Persistence;
using Assetra.Infrastructure.Scheduling;
using Assetra.Infrastructure.Search;
using Assetra.WPF.Features.AddStock;
using Assetra.WPF.Features.Alerts;
using Assetra.WPF.Features.Allocation;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Features.Settings;
using Assetra.WPF.Features.Snackbar;
using Assetra.WPF.Features.StatusBar;
using Assetra.WPF.Shell;

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
        var services = builder.Services;

        // Logging
        services.AddLogging(b => b.AddSerilog(dispose: true));

        // Localization & Theme — must be registered before any ViewModel that depends on them
        services.AddSingleton<ILocalizationService, WpfLocalizationService>();
        services.AddSingleton<IThemeService, AppThemeService>();

        // HTTP — shared browser-like User-Agent for external APIs
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

        // SQLite — all repositories share the same DB file
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Assetra");
        Directory.CreateDirectory(dataDir);
        var dbPath = Path.Combine(dataDir, "assetra.db");

        services.AddSingleton<IStockSearchService>(_ => new StockSearchService(assetsDir));

        // UI scheduler (switches to WPF dispatcher thread)
        services.AddSingleton<IScheduler>(_ =>
            new DispatcherScheduler(System.Windows.Application.Current.Dispatcher));

        // StockScheduler is internal — factory registration. Assetra's scheduler
        // derives its watch set from IPortfolioRepository only (no watchlist concept).
        services.AddSingleton<IStockService>(sp => new StockScheduler(
            sp.GetRequiredService<ITwseClient>(),
            sp.GetRequiredService<ITpexClient>(),
            sp.GetRequiredService<IPortfolioRepository>(),
            sp.GetRequiredService<IScheduler>()));

        // Settings service
        services.AddSingleton<IAppSettingsService>(sp =>
            new AppSettingsService(sp.GetRequiredService<ILogger<AppSettingsService>>()));

        // Currency service — must be registered after IAppSettingsService
        services.AddSingleton<ICurrencyService>(sp =>
            new CurrencyService(
                sp.GetRequiredService<IAppSettingsService>(),
                sp.GetRequiredService<HttpClient>(),
                sp.GetRequiredService<ILogger<CurrencyService>>()));

        // Shared FinMind availability state — both history provider and data service update it
        services.AddSingleton<FinMindApiStatus>();

        // FinMindService is registered first so DynamicHistoryProvider can depend on it.
        // Assetra uses FinMind anonymously (empty token). The Settings UI does not expose
        // a token field; see Features/Settings/SettingsViewModel for the trimmed surface.
        services.AddSingleton<FinMindService>(sp =>
            new FinMindService(
                sp.GetRequiredService<HttpClient>(),
                token: string.Empty,
                sp.GetRequiredService<FinMindApiStatus>(),
                sp.GetRequiredService<ILogger<FinMindService>>()));
        services.AddSingleton<IFinMindService>(sp => sp.GetRequiredService<FinMindService>());

        // History provider — DynamicHistoryProvider reads IAppSettingsService.Current at call time
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

            return new DynamicHistoryProvider(http, settingsSvc, finMindService, finMindStatus);
        });

        // In-app Snackbar service
        services.AddSingleton<SnackbarViewModel>();
        services.AddSingleton<ISnackbarService>(sp =>
            new SnackbarService(sp.GetRequiredService<SnackbarViewModel>()));

        // Persistence — Portfolio + Alerts + Assets + Trades (SQLite, same DB file)
        services.AddSingleton<IPortfolioRepository>(_ => new PortfolioSqliteRepository(dbPath));
        services.AddSingleton<IPortfolioSnapshotRepository>(_ => new PortfolioSnapshotSqliteRepository(dbPath));
        services.AddSingleton<IPortfolioPositionLogRepository>(_ => new PortfolioPositionLogSqliteRepository(dbPath));
        services.AddSingleton<PortfolioSnapshotService>();
        services.AddSingleton<PortfolioBackfillService>();
        services.AddSingleton<IAlertRepository>(_ => new AlertSqliteRepository(dbPath));
        services.AddSingleton<ITradeRepository>(_ => new TradeSqliteRepository(dbPath));
        services.AddSingleton<IAssetRepository>(_ => new AssetSqliteRepository(dbPath));
        services.AddSingleton<ILoanScheduleRepository>(_ => new LoanScheduleSqliteRepository(dbPath));
        services.AddSingleton<ITransactionService>(sp => new TransactionService(
            sp.GetRequiredService<ITradeRepository>()));
        services.AddSingleton<IBalanceQueryService>(sp =>
            new BalanceQueryService(sp.GetRequiredService<ITradeRepository>()));
        services.AddSingleton<IPositionQueryService>(sp =>
            new Assetra.Infrastructure.PositionQueryService(
                sp.GetRequiredService<ITradeRepository>()));
        services.AddSingleton<IPortfolioLoadService>(sp => new PortfolioLoadService(
            sp.GetRequiredService<IPortfolioRepository>(),
            sp.GetRequiredService<IPositionQueryService>(),
            sp.GetRequiredService<ITradeRepository>(),
            sp.GetRequiredService<IBalanceQueryService>(),
            sp.GetService<IAssetRepository>()));
        services.AddSingleton<IPortfolioSummaryService, PortfolioSummaryService>();
        services.AddSingleton<IAddAssetWorkflowService>(sp => new AddAssetWorkflowService(
            sp.GetRequiredService<IStockSearchService>(),
            sp.GetService<IStockHistoryProvider>(),
            sp.GetRequiredService<IPortfolioRepository>(),
            sp.GetRequiredService<IPortfolioPositionLogRepository>(),
            sp.GetRequiredService<ITransactionService>()));
        services.AddSingleton<ITransactionWorkflowService, TransactionWorkflowService>();
        services.AddSingleton<ICryptoService, CoinGeckoService>();

        // Stockra import service — target is our own assetra.db
        services.AddSingleton<IStockraImportService>(_ => new StockraImportService(dbPath));

        // ViewModels
        services.AddSingleton<NavRailViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<StatusBarViewModel>();
        services.AddSingleton<PortfolioViewModel>(sp => new PortfolioViewModel(
            new PortfolioRepositories(
                sp.GetRequiredService<IPortfolioRepository>(),
                sp.GetRequiredService<IPortfolioSnapshotRepository>(),
                sp.GetRequiredService<IPortfolioPositionLogRepository>(),
                sp.GetRequiredService<ITradeRepository>(),
                sp.GetRequiredService<IAssetRepository>(),
                sp.GetRequiredService<ILoanScheduleRepository>()),
            new PortfolioServices(
                sp.GetRequiredService<IStockService>(),
                sp.GetRequiredService<IStockSearchService>(),
                sp.GetRequiredService<PortfolioSnapshotService>(),
                sp.GetRequiredService<PortfolioBackfillService>(),
                sp.GetRequiredService<IPortfolioLoadService>(),
                sp.GetRequiredService<IAddAssetWorkflowService>(),
                sp.GetRequiredService<IStockHistoryProvider>(),
                sp.GetRequiredService<ICurrencyService>(),
                sp.GetRequiredService<ICryptoService>(),
                sp.GetRequiredService<ITransactionService>(),
                sp.GetRequiredService<IBalanceQueryService>(),
                sp.GetRequiredService<IPositionQueryService>(),
                sp.GetRequiredService<ITransactionWorkflowService>(),
                sp.GetRequiredService<IPortfolioSummaryService>()),
            new PortfolioUiServices(
                System.Reactive.Concurrency.DefaultScheduler.Instance,
                sp.GetService<IThemeService>(),
                sp.GetRequiredService<IAppSettingsService>(),
                sp.GetRequiredService<ISnackbarService>(),
                sp.GetRequiredService<ILocalizationService>())));
        services.AddSingleton<AllocationViewModel>(sp => new AllocationViewModel(
            sp.GetRequiredService<PortfolioViewModel>(),
            sp.GetRequiredService<IAppSettingsService>()));
        services.AddSingleton<DashboardViewModel>(sp => new DashboardViewModel(
            sp.GetRequiredService<PortfolioViewModel>(),
            sp.GetService<IThemeService>()));
        services.AddSingleton<FinancialOverviewViewModel>(sp => new FinancialOverviewViewModel(
            sp.GetRequiredService<IAssetRepository>(),
            sp.GetRequiredService<IBalanceQueryService>(),
            sp.GetRequiredService<PortfolioViewModel>()));
        services.AddSingleton<AlertsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        // Singleton: CLAUDE.md mandates all VMs be singletons; Reset() is called on dialog open.
        services.AddSingleton<AddStockViewModel>();

        // Views
        services.AddSingleton<MainWindow>(sp =>
            new MainWindow(sp.GetRequiredService<MainViewModel>(), sp));

        services.AddHostedService<DbInitializerService>();
        services.AddHostedService<MarketDataHostedService>();

        var host = builder.Build();
        var provider = host.Services;

        // Apply colour-scheme convention before the first window renders
        var themeService = provider.GetRequiredService<IThemeService>();
        ColorSchemeService.Apply(savedSettings.TaiwanColorScheme, themeService.CurrentTheme);

        // Apply saved language (swaps the language ResourceDictionary loaded in App.xaml)
        var localization = provider.GetRequiredService<ILocalizationService>();
        if (!string.IsNullOrWhiteSpace(savedSettings.Language) && savedSettings.Language != "zh-TW")
            localization.SetLanguage(savedSettings.Language);

        // Refresh exchange rates in the background on startup — silently falls back to defaults on failure
        _ = Task.Run(async () =>
        {
            try
            {
                await provider.GetRequiredService<ICurrencyService>().RefreshRatesAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Background task {Task} failed at startup", nameof(ICurrencyService.RefreshRatesAsync));
            }
        });

        // Download fresh stock lists from TWSE/TPEX APIs, then warm up search service
        _ = Task.Run(async () =>
        {
            try
            {
                var updated = await StockListDownloader.UpdateAsync(assetsDir);
                if (updated)
                {
                    // Reload search service with fresh data
                    var search = provider.GetRequiredService<IStockSearchService>();
                    if (search is StockSearchService svc)
                        svc.Reload(assetsDir);
                }
                provider.GetRequiredService<IStockSearchService>().GetAll();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Background task {Task} failed at startup", nameof(StockListDownloader.UpdateAsync));
            }
        });

        return host;
    }
}
