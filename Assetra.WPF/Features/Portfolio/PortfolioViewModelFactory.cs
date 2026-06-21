using System.Reactive.Concurrency;
using Assetra.Application.Fx;
using Assetra.Application.Loans.Contracts;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Core.DomainServices;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.WPF.Features.Portfolio.SubViewModels;
using Assetra.WPF.Features.PortfolioGroups;
using Assetra.WPF.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// Builds <see cref="PortfolioViewModel"/> from the DI container. The recipe used to live
/// inline inside <c>PortfolioServiceCollectionExtensions.AddPortfolioContext</c>; pulling it
/// out gives tests a single source of truth for what the production constructor expects
/// (see docs/planning/H3-PortfolioViewModelFactory-Plan.md).
/// </summary>
internal sealed class PortfolioViewModelFactory
{
    private readonly IServiceProvider _sp;

    public PortfolioViewModelFactory(IServiceProvider sp) => _sp = sp;

    public PortfolioViewModel Create() =>
        new(BuildPortfolioServices(_sp), BuildPortfolioUiServices(_sp));

    // internal (not private) so a regression test can assert every workflow service the
    // production ctor needs is wired — a missing one (e.g. PositionMetadata) silently
    // falls back to a Null no-op service and drops writes.
    internal static PortfolioServices BuildPortfolioServices(IServiceProvider sp) =>
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
            // Stage 1 (Dashboard consolidation)：把分析服務灌進 PortfolioHistory，
            // 讓 Trends 頁顯示最大回撤與對標 TWR。
            Drawdown: sp.GetService<IDrawdownCalculator>(),
            Benchmark: sp.GetService<IBenchmarkComparisonService>(),
            // Stage 1 finish：full TWR 用的計算器 + 交易資料源。
            Twr: sp.GetService<ITimeWeightedReturnCalculator>(),
            Trades: sp.GetService<ITradeRepository>(),
            // 風險指標（從 Reports 搬到 Trends 共用）
            Volatility: sp.GetService<IVolatilityCalculator>(),
            Sharpe: sp.GetService<ISharpeRatioCalculator>(),
            Concentration: sp.GetService<IConcentrationAnalyzer>(),
            Crypto: sp.GetRequiredService<ICryptoService>(),
            BalanceQuery: sp.GetRequiredService<IBalanceQueryService>(),
            PositionQuery: sp.GetRequiredService<IPositionQueryService>(),
            TransactionWorkflow: sp.GetRequiredService<ITransactionWorkflowService>(),
            AccountUpsert: sp.GetRequiredService<IAccountUpsertWorkflowService>(),
            AccountMutation: sp.GetRequiredService<IAccountMutationWorkflowService>(),
            // 必須注入：缺了它時 2-arg ctor 會 fallback 到 NullPositionMetadataWorkflowService，
            // 導致「移至投資組合」靜默不寫 DB（in-session 看似成功、重啟即失）。
            PositionMetadata: sp.GetRequiredService<IPositionMetadataWorkflowService>(),
            // 必須注入：缺了它時 fallback 到 NullTradeMetadataWorkflowService（UpdateAsync 永遠回 false），
            // 導致在「交易記錄」編輯日期/備註一律失敗「找不到此筆記錄或記錄已被修改」。
            TradeMetadata: sp.GetRequiredService<ITradeMetadataWorkflowService>(),
            LiabilityMutation: sp.GetRequiredService<ILiabilityMutationWorkflowService>(),
            CreditCardMutation: sp.GetRequiredService<ICreditCardMutationWorkflowService>(),
            CreditCardTransaction: sp.GetRequiredService<ICreditCardTransactionWorkflowService>(),
            LoanPayment: sp.GetRequiredService<ILoanPaymentWorkflowService>(),
            LoanMutation: sp.GetRequiredService<ILoanMutationWorkflowService>(),
            AddAsset: sp.GetRequiredService<IAddAssetWorkflowService>(),
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
                sp.GetRequiredService<ILocalizationService>()),
            // Portfolio-Groups-Refactor P3 — 共用群組目錄注入。
            GroupCatalog: sp.GetService<PortfolioGroupCatalog>(),
            GroupPerformance: sp.GetService<IGroupPerformanceSeriesService>(),
            // P4.1 — Asset-level XIRR 年化報酬計算（detail panel KPI 矩陣使用）。
            Xirr: sp.GetService<IXirrCalculator>(),
            TransactionFxRateResolver: sp.GetService<TransactionFxRateResolver>())
        {
            Summary = sp.GetRequiredService<IPortfolioSummaryService>(),
        };

    private static PortfolioUiServices BuildPortfolioUiServices(IServiceProvider sp) =>
        new PortfolioUiServices(
            DefaultScheduler.Instance,
            sp.GetService<IThemeService>(),
            sp.GetRequiredService<IAppSettingsService>(),
            sp.GetRequiredService<ISnackbarService>(),
            sp.GetRequiredService<ILocalizationService>());
}
