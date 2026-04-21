using System.Reactive.Concurrency;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Core.DomainServices;
using Assetra.Core.Interfaces;
using Assetra.WPF.Infrastructure;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// Data-access dependencies for <see cref="PortfolioViewModel"/>.
/// Optional repos stay nullable so tests can omit them without fabricating doubles.
/// </summary>
public sealed record PortfolioRepositories(
    IPortfolioRepository Portfolio,
    IPortfolioSnapshotRepository Snapshot,
    IPortfolioPositionLogRepository PositionLog,
    ITradeRepository? Trade = null,
    IAssetRepository? Asset = null,
    ILoanScheduleRepository? LoanSchedule = null);

/// <summary>
/// Domain services the ViewModel fans out to (quotes, snapshots, FX, crypto, transactions, balance projection).
/// </summary>
public sealed record PortfolioServices(
    IStockService Stock,
    IStockSearchService Search,
    IPortfolioHistoryMaintenanceService? HistoryMaintenance = null,
    IPortfolioHistoryQueryService? HistoryQuery = null,
    IPortfolioLoadService? Load = null,
    IAddAssetWorkflowService? AddAssetWorkflow = null,
    ITradeMetadataWorkflowService? TradeMetadataWorkflow = null,
    IStockHistoryProvider? History = null,
    ICurrencyService? Currency = null,
    ICryptoService? Crypto = null,
    /// <summary>
    /// 餘額投影服務：由交易歷史計算現金 / 負債餘額。
    /// 測試中若省略，ViewModel 會回退為 <c>NullBalanceQueryService</c>（回傳 0）。
    /// </summary>
    IBalanceQueryService? BalanceQuery = null,
    /// <summary>
    /// 持倉投影服務：由交易歷史計算每個 PortfolioEntry 的持倉數量與成本。
    /// 測試中若省略，ViewModel 會回退為 <c>NullPositionQueryService</c>（回傳空快照）。
    /// </summary>
    IPositionQueryService? PositionQuery = null,
    /// <summary>
    /// 交易流程服務：直接執行 income / cash-flow / dividend / transfer 的交易寫入。
    /// 貸款相關操作由 <see cref="ILoanMutationWorkflowService"/> 負責。
    /// 測試中若省略，ViewModel 會回退為內建實作。
    /// </summary>
    ITransactionWorkflowService? TransactionWorkflow = null,
    /// <summary>
    /// 投組摘要計算服務：統一計算 totals、allocation 與財務摘要指標。
    /// 測試中若省略，ViewModel 會回退為內建實作。
    /// </summary>
    IPortfolioSummaryService? Summary = null,
    ITradeDeletionWorkflowService? TradeDeletionWorkflow = null,
    IPositionDeletionWorkflowService? PositionDeletionWorkflow = null,
    ISellWorkflowService? SellWorkflow = null,
    IPositionMetadataWorkflowService? PositionMetadataWorkflow = null,
    IAccountMutationWorkflowService? AccountMutationWorkflow = null,
    IAccountUpsertWorkflowService? AccountUpsertWorkflow = null,
    ILoanPaymentWorkflowService? LoanPaymentWorkflow = null,
    ILoanMutationWorkflowService? LoanMutationWorkflow = null);

/// <summary>
/// UI-adjacent services (scheduler / theming / settings / snackbar / localization).
/// </summary>
public sealed record PortfolioUiServices(
    IScheduler Scheduler,
    IThemeService? Theme = null,
    IAppSettingsService? Settings = null,
    ISnackbarService? Snackbar = null,
    ILocalizationService? Localization = null);
