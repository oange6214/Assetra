using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.DomainServices;
using Assetra.Core.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// PortfolioViewModel partial — null-object fallback service implementations used when
/// DI does not wire a concrete service (notably in tests). All methods return empty / 0 /
/// no-op results so the ViewModel can run without a populated repository.
/// </summary>
public partial class PortfolioViewModel
{
    // Fallback when DI doesn't wire IBalanceQueryService (tests that only populate
    // stored account rows without a trade history). All balances return 0 / empty.
    private sealed class NullBalanceQueryService : IBalanceQueryService
    {
        public Task<decimal> GetCashBalanceAsync(Guid cashAccountId) =>
            Task.FromResult(0m);
        public Task<LiabilitySnapshot> GetLiabilitySnapshotAsync(string loanLabel) =>
            Task.FromResult(LiabilitySnapshot.Empty);
        public Task<IReadOnlyDictionary<Guid, decimal>> GetAllCashBalancesAsync() =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(
                new Dictionary<Guid, decimal>());
        public Task<IReadOnlyDictionary<string, LiabilitySnapshot>> GetAllLiabilitySnapshotsAsync() =>
            Task.FromResult<IReadOnlyDictionary<string, LiabilitySnapshot>>(
                new Dictionary<string, LiabilitySnapshot>());
    }

    // Fallback when DI doesn't wire IPositionQueryService (tests that don't seed trades).
    // All snapshots return 0 / empty.
    private sealed class NullPositionQueryService : IPositionQueryService
    {
        public Task<PositionSnapshot?> GetPositionAsync(Guid portfolioEntryId) =>
            Task.FromResult<PositionSnapshot?>(null);
        public Task<IReadOnlyDictionary<Guid, PositionSnapshot>> GetAllPositionSnapshotsAsync() =>
            Task.FromResult<IReadOnlyDictionary<Guid, PositionSnapshot>>(
                new Dictionary<Guid, PositionSnapshot>());
        public Task<decimal> ComputeRealizedPnlAsync(
            Guid portfolioEntryId, DateTime sellDate, decimal sellPrice,
            decimal sellQty, decimal sellFees) =>
            Task.FromResult(0m);
    }

    private sealed class NullPortfolioHistoryMaintenanceService : IPortfolioHistoryMaintenanceService
    {
        public Task<bool> TryRecordSnapshotAsync(
            decimal totalCost,
            decimal marketValue,
            decimal pnl,
            int positionCount,
            string currency = "TWD",
            CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<int> BackfillAsync(CancellationToken ct = default) =>
            Task.FromResult(0);
    }

    private sealed class NullAccountMutationWorkflowService : IAccountMutationWorkflowService
    {
        public Task ArchiveAsync(Guid accountId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<AccountDeletionResult> DeleteAsync(Guid accountId, CancellationToken ct = default) =>
            Task.FromResult(new AccountDeletionResult(false));
    }

    private sealed class NullLiabilityMutationWorkflowService : ILiabilityMutationWorkflowService
    {
        public Task<LiabilityDeletionResult> DeleteAsync(LiabilityDeletionRequest request, CancellationToken ct = default) =>
            Task.FromResult(new LiabilityDeletionResult(false));
    }

    private sealed class NullAccountUpsertWorkflowService : IAccountUpsertWorkflowService
    {
        public Task<AccountUpsertResult> CreateAsync(CreateAccountRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AccountUpsertResult(
                new AssetItem(Guid.NewGuid(), request.Name, FinancialType.Asset, null, request.Currency, request.CreatedDate)));

        public Task<AccountUpsertResult> UpdateAsync(UpdateAccountRequest request, CancellationToken ct = default) =>
            Task.FromResult(new AccountUpsertResult(
                new AssetItem(request.AccountId, request.Name, FinancialType.Asset, null, request.Currency, request.CreatedDate)));

        public Task<Guid> FindOrCreateAccountAsync(string name, string currency, CancellationToken ct = default) =>
            Task.FromResult(Guid.NewGuid());
    }

    private sealed class NullLoanPaymentWorkflowService : ILoanPaymentWorkflowService
    {
        public Task<LoanPaymentResult> RecordAsync(LoanPaymentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new LoanPaymentResult(
                new Trade(
                    Guid.NewGuid(),
                    string.Empty,
                    string.Empty,
                    request.LoanLabel,
                    TradeType.LoanRepay,
                    request.TradeDate,
                    request.Entry.PrincipalAmount + request.Entry.InterestAmount,
                    1,
                    0m,
                    0m,
                    request.Entry.PrincipalAmount + request.Entry.InterestAmount,
                    request.CashAccountId,
                    LoanLabel: request.LoanLabel,
                    Principal: request.Entry.PrincipalAmount,
                    InterestPaid: request.Entry.InterestAmount),
                DateTime.UtcNow));
    }

    private sealed class NullLoanMutationWorkflowService : ILoanMutationWorkflowService
    {
        public Task<LoanMutationResult> RecordAsync(LoanTransactionRequest request, CancellationToken ct = default) =>
            Task.FromResult(new LoanMutationResult(null, null));
    }

    private sealed class NullCreditCardMutationWorkflowService : ICreditCardMutationWorkflowService
    {
        public Task<CreditCardUpsertResult> CreateAsync(CreateCreditCardRequest request, CancellationToken ct = default) =>
            Task.FromResult(new CreditCardUpsertResult(
                new AssetItem(
                    Guid.NewGuid(),
                    request.Name,
                    FinancialType.Liability,
                    null,
                    request.Currency,
                    request.CreatedDate,
                    LiabilitySubtype: LiabilitySubtype.CreditCard,
                    BillingDay: request.BillingDay,
                    DueDay: request.DueDay,
                    CreditLimit: request.CreditLimit,
                    IssuerName: request.IssuerName)));

        public Task<CreditCardUpsertResult> UpdateAsync(UpdateCreditCardRequest request, CancellationToken ct = default) =>
            Task.FromResult(new CreditCardUpsertResult(
                new AssetItem(
                    request.CardId,
                    request.Name,
                    FinancialType.Liability,
                    null,
                    request.Currency,
                    request.CreatedDate,
                    LiabilitySubtype: LiabilitySubtype.CreditCard,
                    BillingDay: request.BillingDay,
                    DueDay: request.DueDay,
                    CreditLimit: request.CreditLimit,
                    IssuerName: request.IssuerName)));
    }

    private sealed class NullCreditCardTransactionWorkflowService : ICreditCardTransactionWorkflowService
    {
        public Task<CreditCardTransactionResult> ChargeAsync(CreditCardChargeRequest request, CancellationToken ct = default) =>
            Task.FromResult(new CreditCardTransactionResult(
                new Trade(
                    Guid.NewGuid(),
                    string.Empty,
                    string.Empty,
                    request.CardName,
                    TradeType.CreditCardCharge,
                    request.TradeDate,
                    request.Amount,
                    1,
                    0m,
                    0m,
                    request.Amount,
                    LiabilityAssetId: request.CreditCardAssetId,
                    Note: request.Note)));

        public Task<CreditCardTransactionResult> PayAsync(CreditCardPaymentRequest request, CancellationToken ct = default) =>
            Task.FromResult(new CreditCardTransactionResult(
                new Trade(
                    Guid.NewGuid(),
                    string.Empty,
                    string.Empty,
                    request.CardName,
                    TradeType.CreditCardPayment,
                    request.TradeDate,
                    request.Amount,
                    1,
                    0m,
                    0m,
                    request.Amount,
                    request.CashAccountId,
                    LiabilityAssetId: request.CreditCardAssetId,
                    Note: request.Note)));
    }

    private sealed class NullPortfolioLoadService : IPortfolioLoadService
    {
        public Task<PortfolioLoadResult> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult(new PortfolioLoadResult(
                [],
                new Dictionary<Guid, PositionSnapshot>(),
                [],
                [],
                new Dictionary<Guid, decimal>(),
                new Dictionary<string, LiabilitySnapshot>(),
                new Dictionary<string, AssetItem>()));
    }

    private sealed class NullPortfolioHistoryQueryService : IPortfolioHistoryQueryService
    {
        public Task<IReadOnlyList<PortfolioDailySnapshot>> GetSnapshotsAsync(
            DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PortfolioDailySnapshot>>([]);
    }

    private sealed class NullTransactionWorkflowService : ITransactionWorkflowService
    {
        public Task RecordCashDividendAsync(CashDividendTransactionRequest request, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordStockDividendAsync(StockDividendTransactionRequest request, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordIncomeAsync(IncomeTransactionRequest request, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordCashFlowAsync(CashFlowTransactionRequest request, CancellationToken ct = default) => Task.CompletedTask;
        public Task RecordTransferAsync(TransferTransactionRequest request, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullTradeDeletionWorkflowService : ITradeDeletionWorkflowService
    {
        public Task<TradeDeletionResult> DeleteAsync(TradeDeletionRequest request, CancellationToken ct = default) =>
            Task.FromResult(new TradeDeletionResult(Success: false));
    }

    private sealed class NullPositionDeletionWorkflowService : IPositionDeletionWorkflowService
    {
        public Task DeleteAsync(PositionDeletionRequest request, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullAddAssetWorkflowService : IAddAssetWorkflowService
    {
        public IReadOnlyList<StockSearchResult> SearchSymbols(string query, int maxResults = 8) => [];
        public Task<ClosePriceLookupResult> LookupClosePriceAsync(string symbol, DateTime buyDate, CancellationToken ct = default) =>
            Task.FromResult(new ClosePriceLookupResult(false, null, string.Empty));
        public BuyPreviewResult BuildBuyPreview(BuyPreviewRequest request) =>
            new(request.Price * request.Quantity, 0m, request.Price * request.Quantity, request.Price);
        public Task<PortfolioEntry> EnsureStockEntryAsync(EnsureStockEntryRequest request, CancellationToken ct = default) =>
            Task.FromResult(new PortfolioEntry(Guid.NewGuid(), request.Symbol, request.Exchange ?? string.Empty));
        public Task<StockBuyResult> ExecuteStockBuyAsync(StockBuyRequest request, CancellationToken ct = default) =>
            Task.FromResult(new StockBuyResult(
                new PortfolioEntry(Guid.NewGuid(), request.Symbol, request.Exchange ?? string.Empty),
                Commission: 0m,
                CommissionDiscountUsed: request.CommissionDiscount,
                CostPerShare: request.Price));
        public Task<ManualAssetCreateResult> CreateManualAssetAsync(ManualAssetCreateRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ManualAssetCreateResult(
                new PortfolioEntry(Guid.NewGuid(), request.Symbol, request.Exchange),
                new PositionSnapshot(Guid.NewGuid(), request.Quantity, request.TotalCost, request.UnitPrice, 0m, request.AcquiredOn)));
        public string InferExchange(string symbol) => string.Empty;
    }

    private sealed class NullSellWorkflowService : ISellWorkflowService
    {
        public Task<SellWorkflowResult> RecordAsync(SellWorkflowRequest request, CancellationToken ct = default) =>
            Task.FromResult(new SellWorkflowResult(
                new Trade(Guid.NewGuid(), request.Symbol, request.Exchange, request.Name,
                    TradeType.Sell, DateTime.UtcNow, request.SellPrice, request.SellQuantity,
                    0m, 0m),
                RemainingQuantity: 0));
    }

    private sealed class NullTradeMetadataWorkflowService : ITradeMetadataWorkflowService
    {
        public Task<bool> UpdateAsync(TradeMetadataUpdateRequest request, CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    private sealed class NullPositionMetadataWorkflowService : IPositionMetadataWorkflowService
    {
        public Task UpdateAsync(PositionMetadataUpdateRequest request, CancellationToken ct = default) => Task.CompletedTask;
    }
}
