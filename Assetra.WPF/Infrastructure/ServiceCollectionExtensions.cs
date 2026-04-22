using System.Net.Http;
using System.Reactive.Concurrency;
using Assetra.Application.Alerts.Contracts;
using Assetra.Application.Alerts.Services;
using Assetra.Application.Loans.Contracts;
using Assetra.Application.Loans.Services;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.DomainServices;
using Assetra.Core.Interfaces;
using Assetra.Infrastructure;
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
using Assetra.WPF.Features.Portfolio.SubViewModels;
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
        services.AddSingleton<IStockSearchService>(_ => new StockSearchService(assetsDir));
        services.AddSingleton<IScheduler>(_ =>
            new DispatcherScheduler(System.Windows.Application.Current.Dispatcher));
        services.AddSingleton<IStockService>(sp => new StockScheduler(
            sp.GetRequiredService<ITwseClient>(),
            sp.GetRequiredService<ITpexClient>(),
            sp.GetRequiredService<IPortfolioRepository>(),
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

            return new DynamicHistoryProvider(http, settingsSvc, finMindService, finMindStatus);
        });

        services.AddSingleton<SnackbarViewModel>();
        services.AddSingleton<ISnackbarService>(sp =>
            new SnackbarService(sp.GetRequiredService<SnackbarViewModel>()));

        services.AddSingleton<IStockraImportService>(_ => new StockraImportService(dbPath));
        return services;
    }

    public static IServiceCollection AddAssetraDataServices(
        this IServiceCollection services,
        string dbPath)
    {
        services.AddSingleton<IPortfolioRepository>(_ => new PortfolioSqliteRepository(dbPath));
        services.AddSingleton<IPortfolioSnapshotRepository>(_ => new PortfolioSnapshotSqliteRepository(dbPath));
        services.AddSingleton<IPortfolioPositionLogRepository>(_ => new PortfolioPositionLogSqliteRepository(dbPath));
        services.AddSingleton<PortfolioSnapshotService>();
        services.AddSingleton<PortfolioBackfillService>();
        services.AddSingleton<IAlertRepository>(_ => new AlertSqliteRepository(dbPath));
        services.AddSingleton<IAlertService, AlertService>();
        services.AddSingleton<ILoanScheduleService, LoanScheduleService>();
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
        services.AddSingleton<ICryptoService, CoinGeckoService>();

        return services;
    }

    public static IServiceCollection AddAssetraCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<IPortfolioSummaryService, PortfolioSummaryService>();
        return services;
    }

    public static IServiceCollection AddAssetraApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IPortfolioLoadService>(sp => new PortfolioLoadService(
            sp.GetRequiredService<IPortfolioRepository>(),
            sp.GetRequiredService<IPositionQueryService>(),
            sp.GetRequiredService<ITradeRepository>(),
            sp.GetRequiredService<IBalanceQueryService>(),
            sp.GetService<IAssetRepository>()));
        services.AddSingleton<IPortfolioHistoryQueryService>(sp =>
            new PortfolioHistoryQueryService(
                sp.GetRequiredService<IPortfolioSnapshotRepository>()));
        services.AddSingleton<IFinancialOverviewQueryService>(sp =>
            new FinancialOverviewQueryService(
                sp.GetRequiredService<IAssetRepository>(),
                sp.GetRequiredService<IBalanceQueryService>()));
        services.AddSingleton<IPortfolioHistoryMaintenanceService>(sp =>
            new PortfolioHistoryMaintenanceService(
                sp.GetRequiredService<PortfolioSnapshotService>(),
                sp.GetRequiredService<PortfolioBackfillService>()));
        services.AddSingleton<ITradeDeletionWorkflowService>(sp =>
            new TradeDeletionWorkflowService(
                sp.GetRequiredService<ITradeRepository>(),
                sp.GetRequiredService<IPortfolioRepository>(),
                sp.GetRequiredService<IPositionQueryService>()));
        services.AddSingleton<ITradeMetadataWorkflowService>(sp =>
            new TradeMetadataWorkflowService(
                sp.GetRequiredService<ITradeRepository>()));
        services.AddSingleton<ISellWorkflowService>(sp =>
            new SellWorkflowService(
                sp.GetRequiredService<ITradeRepository>(),
                sp.GetRequiredService<IPortfolioRepository>(),
                sp.GetRequiredService<IPortfolioPositionLogRepository>(),
                sp.GetRequiredService<IPositionQueryService>()));
        services.AddSingleton<IPositionDeletionWorkflowService>(sp =>
            new PositionDeletionWorkflowService(
                sp.GetRequiredService<ITradeRepository>(),
                sp.GetRequiredService<IPortfolioRepository>()));
        services.AddSingleton<IPositionMetadataWorkflowService>(sp =>
            new PositionMetadataWorkflowService(
                sp.GetRequiredService<IPortfolioRepository>()));
        services.AddSingleton<IAccountMutationWorkflowService>(sp =>
            new AccountMutationWorkflowService(
                sp.GetRequiredService<IAssetRepository>()));
        services.AddSingleton<IAccountUpsertWorkflowService>(sp =>
            new AccountUpsertWorkflowService(
                sp.GetRequiredService<IAssetRepository>()));
        services.AddSingleton<ILoanPaymentWorkflowService>(sp =>
            new LoanPaymentWorkflowService(
                sp.GetRequiredService<ITradeRepository>(),
                sp.GetRequiredService<ILoanScheduleRepository>()));
        services.AddSingleton<ILoanMutationWorkflowService>(sp =>
            new LoanMutationWorkflowService(
                sp.GetRequiredService<IAssetRepository>(),
                sp.GetRequiredService<ILoanScheduleRepository>(),
                sp.GetRequiredService<ITransactionService>()));
        services.AddSingleton<IAddAssetWorkflowService>(sp => new AddAssetWorkflowService(
            sp.GetRequiredService<IStockSearchService>(),
            sp.GetService<IStockHistoryProvider>(),
            sp.GetRequiredService<IPortfolioRepository>(),
            sp.GetRequiredService<IPortfolioPositionLogRepository>(),
            sp.GetRequiredService<ITransactionService>()));
        services.AddSingleton<ITransactionWorkflowService>(sp =>
            new TransactionWorkflowService(
                sp.GetRequiredService<ITransactionService>()));
        return services;
    }

    public static IServiceCollection AddAssetraViewModels(this IServiceCollection services)
    {
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
                Stock: sp.GetRequiredService<IStockService>(),
                Search: sp.GetRequiredService<IStockSearchService>(),
                HistoryMaintenance: sp.GetRequiredService<IPortfolioHistoryMaintenanceService>(),
                HistoryQuery: sp.GetRequiredService<IPortfolioHistoryQueryService>(),
                TradeDeletionWorkflow: sp.GetRequiredService<ITradeDeletionWorkflowService>(),
                PositionDeletionWorkflow: sp.GetRequiredService<IPositionDeletionWorkflowService>(),
                LoanSchedule: sp.GetRequiredService<ILoanScheduleService>(),
                Load: sp.GetRequiredService<IPortfolioLoadService>(),
                History: sp.GetRequiredService<IStockHistoryProvider>(),
                Currency: sp.GetRequiredService<ICurrencyService>(),
                Crypto: sp.GetRequiredService<ICryptoService>(),
                BalanceQuery: sp.GetRequiredService<IBalanceQueryService>(),
                PositionQuery: sp.GetRequiredService<IPositionQueryService>(),
                TransactionWorkflow: sp.GetRequiredService<ITransactionWorkflowService>(),
                // Pre-built Sub-VMs that don't require parent-VM callbacks at construction
                // time. The parent PortfolioViewModel wires the delegate properties afterward.
                AddAssetDialog: new AddAssetDialogViewModel(
                    sp.GetRequiredService<IAddAssetWorkflowService>(),
                    sp.GetRequiredService<IAccountUpsertWorkflowService>()),
                SellPanel: new SellPanelViewModel(
                    sp.GetRequiredService<ISellWorkflowService>(),
                    new PortfolioSellPanelController(),
                    sp.GetRequiredService<ISnackbarService>(),
                    sp.GetRequiredService<ILocalizationService>()))
            {
                Summary = sp.GetRequiredService<IPortfolioSummaryService>(),
            },
            new PortfolioUiServices(
                DefaultScheduler.Instance,
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
            sp.GetRequiredService<IFinancialOverviewQueryService>(),
            sp.GetRequiredService<PortfolioViewModel>()));
        services.AddSingleton<AlertsViewModel>(sp => new AlertsViewModel(
            sp.GetRequiredService<IAlertService>(),
            sp.GetRequiredService<IStockSearchService>(),
            sp.GetRequiredService<IStockService>(),
            sp.GetRequiredService<IScheduler>(),
            sp.GetRequiredService<ISnackbarService>(),
            sp.GetRequiredService<ILocalizationService>(),
            sp.GetService<ICurrencyService>()));
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<AddStockViewModel>();
        services.AddSingleton<MainWindow>(sp =>
            new MainWindow(sp.GetRequiredService<MainViewModel>(), sp));
        return services;
    }

    public static IServiceCollection AddAssetraHostedServices(this IServiceCollection services)
    {
        services.AddHostedService<DbInitializerService>();
        services.AddHostedService<MarketDataHostedService>();
        return services;
    }
}
