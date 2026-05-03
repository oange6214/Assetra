using System.Reactive.Concurrency;
using Assetra.Application.Loans.Contracts;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.DomainServices;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Interfaces.Sync;
using Assetra.Infrastructure;
using Assetra.Infrastructure.Http;
using Assetra.Infrastructure.Persistence;
using Assetra.WPF.Features.FinancialOverview;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Features.Portfolio.Controls;
using Assetra.WPF.Features.Portfolio.SubViewModels;
using Assetra.WPF.Features.Snackbar;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Infrastructure;

internal static class PortfolioServiceCollectionExtensions
{
    public static IServiceCollection AddPortfolioContext(
        this IServiceCollection services,
        string dbPath)
    {
        // Repositories
        // Same concrete-singleton pattern (v0.20.9): shared instance between
        // IPortfolioRepository and IPortfolioSyncStore.
        services.AddSingleton<PortfolioSqliteRepository>(sp =>
        {
            var settings = sp.GetRequiredService<IAppSettingsService>();
            return new PortfolioSqliteRepository(dbPath, () => SyncDeviceIdProvider.Resolve(settings));
        });
        services.AddSingleton<IPortfolioRepository>(sp => sp.GetRequiredService<PortfolioSqliteRepository>());
        services.AddSingleton<IPortfolioSyncStore>(sp => sp.GetRequiredService<PortfolioSqliteRepository>());
        services.AddSingleton<IPortfolioSnapshotRepository>(_ => new PortfolioSnapshotSqliteRepository(dbPath));
        services.AddSingleton<IPortfolioPositionLogRepository>(_ => new PortfolioPositionLogSqliteRepository(dbPath));
        // Single TradeSqliteRepository instance shared between ITradeRepository (consumers)
        // and ITradeSyncStore (sync layer) — same pattern as Category.
        services.AddSingleton<TradeSqliteRepository>(sp =>
        {
            var settings = sp.GetRequiredService<IAppSettingsService>();
            return new TradeSqliteRepository(dbPath, () => SyncDeviceIdProvider.Resolve(settings));
        });
        services.AddSingleton<ITradeRepository>(sp => sp.GetRequiredService<TradeSqliteRepository>());
        services.AddSingleton<ITradeSyncStore>(sp => sp.GetRequiredService<TradeSqliteRepository>());

        // Same concrete-singleton pattern for Asset (v0.20.8): shared instance between
        // IAssetRepository and IAssetSyncStore.
        services.AddSingleton<AssetSqliteRepository>(sp =>
        {
            var settings = sp.GetRequiredService<IAppSettingsService>();
            return new AssetSqliteRepository(dbPath, () => SyncDeviceIdProvider.Resolve(settings));
        });
        services.AddSingleton<IAssetRepository>(sp => sp.GetRequiredService<AssetSqliteRepository>());
        services.AddSingleton<IAssetSyncStore>(sp => sp.GetRequiredService<AssetSqliteRepository>());
        services.AddSingleton<IAssetGroupSyncStore>(sp => sp.GetRequiredService<AssetSqliteRepository>());
        services.AddSingleton<IAssetEventSyncStore>(sp => sp.GetRequiredService<AssetSqliteRepository>());

        // Domain / infrastructure services
        services.AddSingleton<PortfolioSnapshotService>();
        services.AddSingleton<PortfolioBackfillService>();
        services.AddSingleton<IPortfolioSummaryService, PortfolioSummaryService>();
        services.AddSingleton<ITransactionService>(sp => new TransactionService(
            sp.GetRequiredService<ITradeRepository>()));
        services.AddSingleton<IBalanceQueryService>(sp =>
            new BalanceQueryService(sp.GetRequiredService<ITradeRepository>()));
        services.AddSingleton<IPositionQueryService>(sp =>
            new Assetra.Infrastructure.PositionQueryService(
                sp.GetRequiredService<ITradeRepository>()));
        services.AddSingleton<ICryptoService, CoinGeckoService>();

        // Application workflow / query services
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
                sp.GetRequiredService<IBalanceQueryService>(),
                sp.GetService<IMultiCurrencyValuationService>(),
                sp.GetService<IAppSettingsService>()));
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
                sp.GetRequiredService<IAssetRepository>(),
                sp.GetRequiredService<ITradeRepository>()));
        services.AddSingleton<ILiabilityMutationWorkflowService>(sp =>
            new LiabilityMutationWorkflowService(
                sp.GetRequiredService<IAssetRepository>(),
                sp.GetRequiredService<ITradeRepository>()));
        services.AddSingleton<IAccountUpsertWorkflowService>(sp =>
            new AccountUpsertWorkflowService(
                sp.GetRequiredService<IAssetRepository>()));
        services.AddSingleton<ICreditCardMutationWorkflowService>(sp =>
            new CreditCardMutationWorkflowService(
                sp.GetRequiredService<IAssetRepository>()));
        services.AddSingleton<ICreditCardTransactionWorkflowService>(sp =>
            new CreditCardTransactionWorkflowService(
                sp.GetRequiredService<IAssetRepository>(),
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

        // ViewModels
        services.AddSingleton<PortfolioViewModel>(sp => new PortfolioViewModel(
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
                Fx: sp.GetService<IMultiCurrencyValuationService>(),
                Crypto: sp.GetRequiredService<ICryptoService>(),
                BalanceQuery: sp.GetRequiredService<IBalanceQueryService>(),
                PositionQuery: sp.GetRequiredService<IPositionQueryService>(),
                TransactionWorkflow: sp.GetRequiredService<ITransactionWorkflowService>(),
                AccountMutation: sp.GetRequiredService<IAccountMutationWorkflowService>(),
                LiabilityMutation: sp.GetRequiredService<ILiabilityMutationWorkflowService>(),
                CreditCardMutation: sp.GetRequiredService<ICreditCardMutationWorkflowService>(),
                CreditCardTransaction: sp.GetRequiredService<ICreditCardTransactionWorkflowService>(),
                LoanPayment: sp.GetRequiredService<ILoanPaymentWorkflowService>(),
                LoanMutation: sp.GetRequiredService<ILoanMutationWorkflowService>(),
                CategoryRepository: sp.GetRequiredService<ICategoryRepository>(),
                AutoCategorizationRuleRepository: sp.GetRequiredService<IAutoCategorizationRuleRepository>(),
                AddAssetDialog: new AddAssetDialogViewModel(
                    sp.GetRequiredService<IAddAssetWorkflowService>(),
                    sp.GetRequiredService<IAccountUpsertWorkflowService>(),
                    sp.GetRequiredService<ITransactionWorkflowService>(),
                    sp.GetRequiredService<ICreditCardMutationWorkflowService>(),
                    sp.GetRequiredService<ICreditCardTransactionWorkflowService>(),
                    sp.GetRequiredService<ILoanMutationWorkflowService>()),
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
            sp.GetService<IThemeService>(),
            sp.GetService<BudgetSummaryCardViewModel>()));
        services.AddSingleton<FinancialOverviewViewModel>(sp => new FinancialOverviewViewModel(
            sp.GetRequiredService<IFinancialOverviewQueryService>(),
            sp.GetRequiredService<PortfolioViewModel>()));

        return services;
    }
}
