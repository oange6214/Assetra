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
            new BalanceQueryService(
                sp.GetRequiredService<ITradeRepository>(),
                sp.GetService<IAssetRepository>()));
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
        // Append-only audit log: optional dependency of TradeDeletionWorkflowService.
        // Deletes capture a JSON snapshot of the trade BEFORE removal so users can
        // recover from accidental edits / deletes. UI: NavSection.AuditLog page.
        services.AddSingleton<ITradeAuditRepository>(_ => new TradeAuditSqliteRepository(dbPath));
        services.AddSingleton<TradeAuditRestoreService>(sp => new TradeAuditRestoreService(
            sp.GetRequiredService<ITradeRepository>()));
        services.AddSingleton<Features.AuditLog.AuditLogViewModel>(sp =>
            new Features.AuditLog.AuditLogViewModel(
                sp.GetService<ITradeAuditRepository>(),
                sp.GetService<TradeAuditRestoreService>(),
                sp.GetService<ISnackbarService>()));
        services.AddSingleton<ITradeDeletionWorkflowService>(sp =>
            new TradeDeletionWorkflowService(
                sp.GetRequiredService<ITradeRepository>(),
                sp.GetRequiredService<IPortfolioRepository>(),
                sp.GetRequiredService<IPositionQueryService>(),
                sp.GetRequiredService<ITradeAuditRepository>()));
        services.AddSingleton<ITradeMetadataWorkflowService>(sp =>
            new TradeMetadataWorkflowService(
                sp.GetRequiredService<ITradeRepository>()));
        services.AddSingleton<ISellWorkflowService>(sp =>
            new SellWorkflowService(
                sp.GetRequiredService<ITradeRepository>(),
                sp.GetRequiredService<IPortfolioRepository>(),
                sp.GetRequiredService<IPortfolioPositionLogRepository>(),
                sp.GetRequiredService<IPositionQueryService>(),
                // P4.5b — optional FX history for the realized PnL split.
                sp.GetService<IFxRateHistoryService>()));
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
                sp.GetRequiredService<ITradeRepository>(),
                sp.GetService<Assetra.Application.Loans.Contracts.ILoanScheduleRecomputeService>()));
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
            sp.GetRequiredService<ITransactionService>(),
            sp.GetRequiredService<ISymbolDirectory>()));
        services.AddSingleton<ITransactionWorkflowService>(sp =>
            new TransactionWorkflowService(
                sp.GetRequiredService<ITransactionService>()));

        // ViewModels
        services.AddSingleton<PortfolioViewModelFactory>();
        services.AddSingleton<PortfolioViewModel>(sp =>
            sp.GetRequiredService<PortfolioViewModelFactory>().Create());
        services.AddSingleton<AllocationViewModel>(sp => new AllocationViewModel(
            sp.GetRequiredService<PortfolioViewModel>(),
            sp.GetRequiredService<IAppSettingsService>(),
            // Portfolio-Groups-Refactor P4 — 給 "依群組" toggle 用。
            sp.GetService<Assetra.WPF.Features.PortfolioGroups.PortfolioGroupCatalog>()));
        services.AddSingleton<DashboardViewModel>(sp => new DashboardViewModel(
            sp.GetRequiredService<PortfolioViewModel>(),
            sp.GetService<IThemeService>()));
        services.AddSingleton<FinancialOverviewViewModel>(sp => new FinancialOverviewViewModel(
            sp.GetRequiredService<IFinancialOverviewQueryService>(),
            sp.GetRequiredService<PortfolioViewModel>(),
            sp.GetService<IAppSettingsService>(),
            // Stage 2.5：總覽 tab 上的 widget；DI 已經有這三個 VM 作為 singleton。
            sp.GetService<Assetra.WPF.Features.Goals.GoalsViewModel>(),
            sp.GetService<Assetra.WPF.Features.Fire.FireViewModel>(),
            sp.GetService<Assetra.WPF.Features.Assistant.AssistantViewModel>(),
            // Long-term refactor：投資焦點卡 — Portfolio.Dashboard tab 移除後，
            // DashboardViewModel 的角色轉為儀表板上的投資 glance summary。
            sp.GetService<DashboardViewModel>(),
            // Phase C：現金 / 負債焦點卡的資料源（PortfolioViewModel 已有所需集合）；
            // 不動產焦點卡的資料源（RealEstateViewModel 已有 PropertyCount / Totals）。
            sp.GetService<PortfolioViewModel>(),
            sp.GetService<Assetra.WPF.Features.RealEstate.RealEstateViewModel>(),
            // Phase C 擴展：保險 / 退休 / 實物資產焦點卡
            sp.GetService<Assetra.WPF.Features.Insurance.InsurancePolicyViewModel>(),
            sp.GetService<Assetra.WPF.Features.Retirement.RetirementViewModel>(),
            sp.GetService<Assetra.WPF.Features.PhysicalAsset.PhysicalAssetViewModel>(),
            // Portfolio-Groups-Refactor P5 — Hero 用 group balance 算 goal 進度。
            sp.GetService<Assetra.Core.Interfaces.IGroupBalanceQueryService>()));

        return services;
    }
}
