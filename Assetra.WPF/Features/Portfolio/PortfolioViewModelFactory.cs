using System.Reactive.Concurrency;
using Assetra.Application.Loans.Contracts;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Core.DomainServices;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Infrastructure;
using Assetra.WPF.Features.Portfolio.Controls;
using Assetra.WPF.Features.Portfolio.SubViewModels;
using Assetra.WPF.Features.Snackbar;
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

    private static PortfolioServices BuildPortfolioServices(IServiceProvider sp) =>
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
        };

    private static PortfolioUiServices BuildPortfolioUiServices(IServiceProvider sp) =>
        new PortfolioUiServices(
            DefaultScheduler.Instance,
            sp.GetService<IThemeService>(),
            sp.GetRequiredService<IAppSettingsService>(),
            sp.GetRequiredService<ISnackbarService>(),
            sp.GetRequiredService<ILocalizationService>());
}
