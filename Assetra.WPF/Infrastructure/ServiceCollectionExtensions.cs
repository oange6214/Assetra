using System.IO;
using System.Net.Http;
using System.Reactive.Concurrency;
using Assetra.Application.MarketData;
using Assetra.Application.Sync;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Sync;
using Assetra.Infrastructure;
using Assetra.Infrastructure.FinMind;
using Assetra.Infrastructure.History;
using Assetra.Infrastructure.Http;
using Assetra.Infrastructure.MarketData;
using Assetra.Infrastructure.Persistence;
using Assetra.Infrastructure.Scheduling;
using Assetra.Infrastructure.Search;
using Assetra.Infrastructure.Sync;
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

        // Phase 1 sync indicator — counter lives at the platform layer because
        // it only needs the dbPath; the aggregator service is registered in
        // AddAssetraSync where BackgroundSyncService is wired.
        services.AddSingleton<Assetra.Core.Interfaces.Sync.IPendingPushCounter>(
            _ => new Assetra.Infrastructure.Sync.SqlitePendingPushCounter(dbPath));

        // MultiCurrency-Reporting P4.1 — historical FX rate store. Used by
        // later P4.x phases for multi-currency reporting aggregation. Repo
        // schema migration runs lazily in the constructor.
        services.AddSingleton<IFxRateHistoryRepository>(
            _ => new FxRateHistorySqliteRepository(dbPath));
        services.AddSingleton<IFxRateHistoryService>(sp =>
            new Assetra.Application.Fx.FxRateHistoryService(
                sp.GetRequiredService<IFxRateHistoryRepository>()));
        services.AddSingleton<Assetra.Application.Fx.TransactionFxRateResolver>();
        // P4.1b — Yahoo fetcher to populate the history store. Manual trigger
        // for now; P4.1c will add a background poll + settings UI button.
        services.AddSingleton<IFxRateHistoryFetcher>(sp =>
            new Assetra.Infrastructure.Fx.YahooFxRateHistoryFetcher(
                sp.GetRequiredService<HttpClient>()));
        // P4.1c — orchestrator called from AppStartupTasks on every startup.
        services.AddSingleton<Assetra.Application.Fx.FxRateHistoryRefresher>(sp =>
            new Assetra.Application.Fx.FxRateHistoryRefresher(
                sp.GetRequiredService<IFxRateHistoryFetcher>(),
                sp.GetRequiredService<IFxRateHistoryRepository>(),
                sp.GetService<IAppSettingsService>())); // P4.1d — persist LastFxRefreshAt

        services.AddSingleton<HttpClient>(_ =>
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/124.0 Safari/537.36");
            // 45s — Twelve Data free tier 偶爾 cold-start 超過 20s（原本的值會 single-attempt 就放棄）。
            // 配合 TwelveDataClient 的 3-attempt retry，整體最差情境約 45s × 3 = 2.25 min。
            client.Timeout = TimeSpan.FromSeconds(45);
            return client;
        });
        services.AddSingleton<ITwseClient, TwseClient>();
        services.AddSingleton<ITpexClient, TpexClient>();
        services.AddSingleton<FugleClient>();
        services.AddSingleton<TwelveDataClient>();
        services.AddSingleton<ITwelveDataQuotaTracker, TwelveDataQuotaTracker>();
        services.AddSingleton<TwelveDataQuoteProvider>();
        services.AddSingleton<ITwelveDataConnectionTester>(sp => sp.GetRequiredService<TwelveDataQuoteProvider>());
        services.AddSingleton<IEquityQuoteProvider>(sp => sp.GetRequiredService<TwelveDataQuoteProvider>());
        // Yahoo Finance — fallback for foreign equities when Twelve Data fails / times out /
        // doesn't have the symbol. Registered AFTER TwelveData so EquityRouter prefers
        // Twelve Data first and only falls through to Yahoo on failure. No API key needed.
        services.AddSingleton<IEquityQuoteProvider, YahooFinanceQuoteProvider>();
        services.AddSingleton<IEquityQuoteProvider, FugleEquityQuoteProvider>();
        services.AddSingleton<IEquityQuoteProvider, TwseEquityQuoteProvider>();
        services.AddSingleton<IEquityQuoteProvider, TpexEquityQuoteProvider>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IEquityQuoteCache, InMemoryEquityQuoteCache>();
        services.AddSingleton<ITradingCalendarService, TradingCalendarService>();
        services.AddSingleton<EquityRouter>();
        services.AddSingleton<IEquityRouter>(sp => sp.GetRequiredService<EquityRouter>());
        services.AddSingleton<IStockSearchService>(_ => new StockSearchService(assetsDir));
        services.AddSingleton<NasdaqSymbolDirectory>(sp => new NasdaqSymbolDirectory(
            Path.Combine(assetsDir, "market-data", "nasdaq"),
            sp.GetRequiredService<HttpClient>()));
        services.AddSingleton<IRefreshableSymbolDirectory>(sp => sp.GetRequiredService<NasdaqSymbolDirectory>());
        services.AddSingleton<StockSearchSymbolDirectory>();
        services.AddSingleton<ISymbolDirectory>(sp => new CompositeSymbolDirectory(
        [
            sp.GetRequiredService<StockSearchSymbolDirectory>(),
            sp.GetRequiredService<NasdaqSymbolDirectory>(),
        ]));
        services.AddSingleton<IScheduler>(_ =>
            new DispatcherScheduler(System.Windows.Application.Current.Dispatcher));
        services.AddSingleton<IStockService>(sp => new StockScheduler(
            sp.GetRequiredService<IEquityRouter>(),
            sp.GetRequiredService<IPortfolioRepository>(),
            sp.GetRequiredService<IAlertRepository>(),
            sp.GetRequiredService<IScheduler>(),
            calendar: sp.GetRequiredService<ITradingCalendarService>(),
            timeProvider: sp.GetRequiredService<TimeProvider>()));

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
        services.AddSingleton<IEquityOhlcCacheRepository>(_ => new EquityOhlcCacheSqliteRepository(dbPath));
        services.AddSingleton<DynamicHistoryProvider>(sp =>
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
        services.AddSingleton<IStockHistoryProvider>(sp => new CachedStockHistoryProvider(
            sp.GetRequiredService<DynamicHistoryProvider>(),
            sp.GetRequiredService<IEquityOhlcCacheRepository>(),
            sp.GetRequiredService<TimeProvider>()));

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

        // Phase 2 sync popover wiring — trigger delegates to BackgroundSyncService.RequestImmediateSync
        // so the popover can fire an out-of-band sync push from a clean abstraction.
        services.AddSingleton<Assetra.WPF.Features.StatusBar.BackgroundSyncTrigger>(sp =>
            new Assetra.WPF.Features.StatusBar.BackgroundSyncTrigger(
                () => sp.GetRequiredService<BackgroundSyncService>().RequestImmediateSync()));
        // Popover 在 sync 未啟用時主按鈕導向 Settings 頁。VM 不直接持 MainViewModel ref（會循環），
        // 改用 lazy callback：點擊時才 resolve NavRailVM 並切到 Settings section。
        services.AddSingleton<Assetra.WPF.Features.StatusBar.SyncStatusPopoverViewModel>(sp =>
            new Assetra.WPF.Features.StatusBar.SyncStatusPopoverViewModel(
                sp.GetRequiredService<Assetra.Core.Interfaces.Sync.IGlobalSyncStatusService>(),
                sp.GetRequiredService<ILocalizationService>(),
                sp.GetService<Assetra.WPF.Features.StatusBar.BackgroundSyncTrigger>(),
                navigateToSettings: () =>
                    sp.GetRequiredService<Assetra.WPF.Shell.NavRailViewModel>().ActiveSection
                        = Assetra.WPF.Shell.NavSection.Settings));
        services.AddSingleton<StatusBarViewModel>();
        services.AddSingleton<SyncSettingsViewModel>();
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

    public static IServiceCollection AddAssetraSync(
        this IServiceCollection services,
        string dataDir)
    {
        services.AddSingleton<IConflictResolver>(_ => new LastWriteWinsResolver());
        services.AddSingleton<CategoryLocalChangeQueue>(sp =>
            new CategoryLocalChangeQueue(sp.GetRequiredService<ICategorySyncStore>()));
        services.AddSingleton<TradeLocalChangeQueue>(sp =>
            new TradeLocalChangeQueue(sp.GetRequiredService<ITradeSyncStore>()));
        services.AddSingleton<AssetLocalChangeQueue>(sp =>
            new AssetLocalChangeQueue(sp.GetRequiredService<IAssetSyncStore>()));
        services.AddSingleton<AssetGroupLocalChangeQueue>(sp =>
            new AssetGroupLocalChangeQueue(sp.GetRequiredService<IAssetGroupSyncStore>()));
        services.AddSingleton<AssetEventLocalChangeQueue>(sp =>
            new AssetEventLocalChangeQueue(sp.GetRequiredService<IAssetEventSyncStore>()));
        services.AddSingleton<PortfolioLocalChangeQueue>(sp =>
            new PortfolioLocalChangeQueue(sp.GetRequiredService<IPortfolioSyncStore>()));
        services.AddSingleton<AutoCategorizationRuleLocalChangeQueue>(sp =>
            new AutoCategorizationRuleLocalChangeQueue(sp.GetRequiredService<IAutoCategorizationRuleSyncStore>()));
        services.AddSingleton<RecurringTransactionLocalChangeQueue>(sp =>
            new RecurringTransactionLocalChangeQueue(sp.GetRequiredService<IRecurringTransactionSyncStore>()));
        // Sync-Goal-PortfolioGroup pass — Goal + PortfolioGroup now ride sync.
        services.AddSingleton<FinancialGoalLocalChangeQueue>(sp =>
            new FinancialGoalLocalChangeQueue(sp.GetRequiredService<IFinancialGoalSyncStore>()));
        services.AddSingleton<PortfolioGroupLocalChangeQueue>(sp =>
            new PortfolioGroupLocalChangeQueue(sp.GetRequiredService<IPortfolioGroupSyncStore>()));
        // Sync-Status-Indicator 補洞 — Alert 加入同步。
        services.AddSingleton<AlertLocalChangeQueue>(sp =>
            new AlertLocalChangeQueue(sp.GetRequiredService<IAlertSyncStore>()));

        services.AddSingleton<CompositeLocalChangeQueue>(sp =>
        {
            var map = new Dictionary<string, ILocalChangeQueue>(StringComparer.Ordinal)
            {
                [CategorySyncMapper.EntityType] = sp.GetRequiredService<CategoryLocalChangeQueue>(),
                [TradeSyncMapper.EntityType] = sp.GetRequiredService<TradeLocalChangeQueue>(),
                [AssetSyncMapper.EntityType] = sp.GetRequiredService<AssetLocalChangeQueue>(),
                [AssetGroupSyncMapper.EntityType] = sp.GetRequiredService<AssetGroupLocalChangeQueue>(),
                [AssetEventSyncMapper.EntityType] = sp.GetRequiredService<AssetEventLocalChangeQueue>(),
                [PortfolioSyncMapper.EntityType] = sp.GetRequiredService<PortfolioLocalChangeQueue>(),
                [AutoCategorizationRuleSyncMapper.EntityType] = sp.GetRequiredService<AutoCategorizationRuleLocalChangeQueue>(),
                [RecurringTransactionSyncMapper.EntityType] = sp.GetRequiredService<RecurringTransactionLocalChangeQueue>(),
                [RealEstateSyncMapper.EntityType] = sp.GetRequiredService<RealEstateLocalChangeQueue>(),
                [InsurancePolicySyncMapper.EntityType] = sp.GetRequiredService<InsurancePolicyLocalChangeQueue>(),
                [RetirementAccountSyncMapper.EntityType] = sp.GetRequiredService<RetirementAccountLocalChangeQueue>(),
                [PhysicalAssetSyncMapper.EntityType] = sp.GetRequiredService<PhysicalAssetLocalChangeQueue>(),
                [Assetra.Infrastructure.Sync.FinancialGoalSyncMapper.EntityType] = sp.GetRequiredService<FinancialGoalLocalChangeQueue>(),
                [Assetra.Infrastructure.Sync.PortfolioGroupSyncMapper.EntityType] = sp.GetRequiredService<PortfolioGroupLocalChangeQueue>(),
                [AlertSyncMapper.EntityType] = sp.GetRequiredService<AlertLocalChangeQueue>(),
            };
            return new CompositeLocalChangeQueue(map);
        });
        services.AddSingleton<ILocalChangeQueue>(sp => sp.GetRequiredService<CompositeLocalChangeQueue>());
        services.AddSingleton<IManualConflictDrain>(sp => sp.GetRequiredService<CompositeLocalChangeQueue>());

        var metadataPath = System.IO.Path.Combine(dataDir, "sync-meta.json");
        services.AddSingleton(sp => new SyncCoordinator(
            sp.GetRequiredService<IAppSettingsService>(),
            sp.GetRequiredService<ILocalChangeQueue>(),
            sp.GetRequiredService<IConflictResolver>(),
            metadataPath));
        services.AddSingleton<SyncPassphraseCache>();
        services.AddSingleton<ConflictResolutionViewModel>();

        // BackgroundSyncService must be a SINGLE instance exposed under two
        // service identities: IHostedService (for the .NET host loop) and
        // IBackgroundSyncSignals (for the status indicator to subscribe to).
        // Register the concrete type as singleton, then forward both interfaces.
        services.AddSingleton<BackgroundSyncService>();
        services.AddSingleton<Assetra.Core.Interfaces.Sync.IBackgroundSyncSignals>(
            sp => sp.GetRequiredService<BackgroundSyncService>());
        services.AddHostedService(sp => sp.GetRequiredService<BackgroundSyncService>());

        // Phase 1 sync status indicator (see docs/planning/Sync-Status-Indicator.md).
        // Counter is registered in AddAssetraPersistence where dbPath is in scope.
        services.AddSingleton<Assetra.Core.Interfaces.Sync.IGlobalSyncStatusService>(sp =>
            new Assetra.Infrastructure.Sync.GlobalSyncStatusService(
                sp.GetRequiredService<Assetra.Core.Interfaces.Sync.IBackgroundSyncSignals>(),
                sp.GetRequiredService<Assetra.Core.Interfaces.Sync.IPendingPushCounter>(),
                sp.GetRequiredService<IScheduler>(),
                initiallyEnabled: sp.GetRequiredService<IAppSettingsService>().Current?.SyncEnabled ?? false));
        return services;
    }
}
