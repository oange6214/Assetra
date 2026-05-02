using System.Collections.ObjectModel;
using Moq;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Features.Portfolio.SubViewModels;
using Xunit;

namespace Assetra.Tests.WPF;

/// <summary>
/// Refactor-guard tests for TransactionDialogViewModel — locks down pure-VM behavior
/// (mode flags, validation, computed previews) before splitting the 1194-line file.
///
/// Strategy: construct the VM with stub services. We do not exercise commands here
/// (those go through workflow services and are covered indirectly by
/// PortfolioViewModelTests end-to-end flows). Instead, we test property-change
/// driven logic that any split must preserve.
/// </summary>
public class TransactionDialogViewModelTests
{
    private static TransactionDialogViewModel CreateVm(
        ObservableCollection<TradeRowViewModel>? trades = null,
        ObservableCollection<PortfolioRowViewModel>? positions = null,
        ObservableCollection<CashAccountRowViewModel>? cashAccounts = null,
        ObservableCollection<LiabilityRowViewModel>? liabilities = null)
    {
        var addAsset = new AddAssetDialogViewModel(
            Mock.Of<IAddAssetWorkflowService>(),
            Mock.Of<IAccountUpsertWorkflowService>(),
            Mock.Of<ITransactionWorkflowService>(),
            Mock.Of<ICreditCardMutationWorkflowService>());
        var sellPanel = new SellPanelViewModel(
            Mock.Of<ISellWorkflowService>(),
            new PortfolioSellPanelController());

        var deps = new TransactionDialogDependencies(
            TransactionWorkflow: Mock.Of<ITransactionWorkflowService>(),
            TradeDeletion: Mock.Of<ITradeDeletionWorkflowService>(),
            TradeMetadata: Mock.Of<ITradeMetadataWorkflowService>(),
            LoanMutation: Mock.Of<ILoanMutationWorkflowService>(),
            CreditCardTransaction: Mock.Of<ICreditCardTransactionWorkflowService>(),
            Search: Mock.Of<IStockSearchService>(),
            TradeDialogController: new PortfolioTradeDialogController(),
            AccountUpsert: null,
            Snackbar: null,
            Trades: trades ?? new(),
            Positions: positions ?? new(),
            CashAccounts: cashAccounts ?? new(),
            Liabilities: liabilities ?? new(),
            AddAssetDialog: addAsset,
            SellPanel: sellPanel,
            GetDefaultCashAccount: () => null,
            LoadLoanScheduleAsync: _ => Task.CompletedTask,
            LoadLiabilitiesAsync: () => Task.CompletedTask,
            LoadPositionsAsync: () => Task.CompletedTask,
            LoadTradesAsync: () => Task.CompletedTask,
            ReloadAccountBalancesAsync: () => Task.CompletedTask,
            RebuildTotals: () => { },
            Localize: (_, fallback) => fallback);

        return new TransactionDialogViewModel(deps);
    }

    // ── Mode flags (TxType change) ───────────────────────────────────────────

    [Fact]
    public void TxType_DefaultsToIncome()
    {
        var vm = CreateVm();
        Assert.Equal("income", vm.TxType);
        Assert.True(vm.TxTypeIsIncome);
        Assert.False(vm.TxTypeIsBuy);
        Assert.False(vm.TxTypeIsSell);
        Assert.False(vm.TxTypeIsLoan);
    }

    [Fact]
    public void TxType_SwitchToBuy_FlagsUpdate()
    {
        var vm = CreateVm();
        vm.TxType = "buy";
        Assert.True(vm.TxTypeIsBuy);
        Assert.False(vm.TxTypeIsIncome);
        Assert.False(vm.TxTypeIsSell);
        Assert.False(vm.TxTypeIsLoan);
    }

    [Fact]
    public void TxType_LoanBorrowAndLoanRepay_BothMapToTxTypeIsLoan()
    {
        var vm = CreateVm();
        vm.TxType = "loanBorrow";
        Assert.True(vm.TxTypeIsLoan);
        Assert.True(vm.TxTypeIsLoanBorrow);
        Assert.False(vm.TxTypeIsLoanRepay);

        vm.TxType = "loanRepay";
        Assert.True(vm.TxTypeIsLoan);
        Assert.False(vm.TxTypeIsLoanBorrow);
        Assert.True(vm.TxTypeIsLoanRepay);
    }

    [Fact]
    public void TxType_DepositAndWithdrawal_BothMapToCashFlow()
    {
        var vm = CreateVm();
        vm.TxType = "deposit";
        Assert.True(vm.TxTypeIsCashFlow);
        vm.TxType = "withdrawal";
        Assert.True(vm.TxTypeIsCashFlow);
        vm.TxType = "transfer";
        Assert.False(vm.TxTypeIsCashFlow);
        Assert.True(vm.TxTypeIsTransfer);
    }

    [Fact]
    public void TxType_CreditCardChargeAndPayment_BothMapToTxTypeIsCreditCard()
    {
        var vm = CreateVm();
        vm.TxType = "creditCardCharge";
        Assert.True(vm.TxTypeIsCreditCard);
        Assert.True(vm.TxTypeIsCreditCardCharge);
        Assert.False(vm.TxTypeIsCreditCardPayment);

        vm.TxType = "creditCardPayment";
        Assert.True(vm.TxTypeIsCreditCard);
        Assert.False(vm.TxTypeIsCreditCardCharge);
        Assert.True(vm.TxTypeIsCreditCardPayment);
    }

    // ── Validation ───────────────────────────────────────────────────────────

    [Fact]
    public void TxAmount_Empty_ClearsError()
    {
        var vm = CreateVm();
        vm.TxAmount = "100";
        vm.TxAmount = "";
        Assert.Equal(string.Empty, vm.TxAmountError);
    }

    [Fact]
    public void TxAmount_NonNumeric_SetsError()
    {
        var vm = CreateVm();
        vm.TxAmount = "abc";
        Assert.NotEqual(string.Empty, vm.TxAmountError);
    }

    [Fact]
    public void TxAmount_ZeroOrNegative_SetsError()
    {
        var vm = CreateVm();
        vm.TxAmount = "0";
        Assert.NotEqual(string.Empty, vm.TxAmountError);
        vm.TxAmount = "-50";
        Assert.NotEqual(string.Empty, vm.TxAmountError);
    }

    [Fact]
    public void TxAmount_PositiveValid_ClearsError()
    {
        var vm = CreateVm();
        vm.TxAmount = "abc";
        Assert.NotEqual(string.Empty, vm.TxAmountError);
        vm.TxAmount = "1234.56";
        Assert.Equal(string.Empty, vm.TxAmountError);
    }

    [Fact]
    public void TxFee_Zero_IsValidNonNegative()
    {
        var vm = CreateVm();
        vm.TxFee = "0";
        Assert.Equal(string.Empty, vm.TxFeeError);
    }

    [Fact]
    public void TxFee_Negative_SetsError()
    {
        var vm = CreateVm();
        vm.TxFee = "-1";
        Assert.NotEqual(string.Empty, vm.TxFeeError);
    }

    [Fact]
    public void TxSellQuantity_NonInteger_SetsError()
    {
        var vm = CreateVm();
        vm.TxSellQuantity = "1.5";
        Assert.NotEqual(string.Empty, vm.TxSellQuantityError);
    }

    [Fact]
    public void TxSellQuantity_PositiveInt_ClearsError()
    {
        var vm = CreateVm();
        vm.TxSellQuantity = "abc";
        Assert.NotEqual(string.Empty, vm.TxSellQuantityError);
        vm.TxSellQuantity = "1000";
        Assert.Equal(string.Empty, vm.TxSellQuantityError);
    }

    // ── Commission discount parsing ──────────────────────────────────────────

    [Fact]
    public void TxCommissionDiscountValue_DefaultsTo1()
    {
        var vm = CreateVm();
        Assert.Equal(1m, vm.TxCommissionDiscountValue);
    }

    [Fact]
    public void TxCommissionDiscountValue_ParsesValid()
    {
        var vm = CreateVm();
        vm.TxCommissionDiscount = "0.65";
        Assert.Equal(0.65m, vm.TxCommissionDiscountValue);
    }

    [Fact]
    public void TxCommissionDiscountValue_OutOfRange_FallsBackTo1()
    {
        var vm = CreateVm();
        vm.TxCommissionDiscount = "1.5";
        Assert.Equal(1m, vm.TxCommissionDiscountValue);
        vm.TxCommissionDiscount = "0";
        Assert.Equal(1m, vm.TxCommissionDiscountValue);
        vm.TxCommissionDiscount = "abc";
        Assert.Equal(1m, vm.TxCommissionDiscountValue);
    }

    [Fact]
    public void TxCommissionDiscount_Invalid_SetsError()
    {
        var vm = CreateVm();
        vm.TxCommissionDiscount = "1.5";
        Assert.NotEqual(string.Empty, vm.TxCommissionDiscountError);
    }

    // ── Edit mode flags ──────────────────────────────────────────────────────

    [Fact]
    public void IsEditMode_FollowsEditingTradeId()
    {
        var vm = CreateVm();
        Assert.False(vm.IsEditMode);
        Assert.True(vm.AreEconomicFieldsEditable);

        vm.EditingTradeId = Guid.NewGuid();
        Assert.True(vm.IsEditMode);
        Assert.False(vm.AreEconomicFieldsEditable);

        vm.EditingTradeId = null;
        Assert.False(vm.IsEditMode);
        Assert.True(vm.AreEconomicFieldsEditable);
    }

    // ── Sell preview (HasTxSellPreview gating) ───────────────────────────────

    [Fact]
    public void HasTxSellPreview_FalseUntilGrossAmountSet()
    {
        var vm = CreateVm();
        Assert.False(vm.HasTxSellPreview);
        vm.TxSellGrossAmount = 100m;
        Assert.True(vm.HasTxSellPreview);
        vm.TxSellGrossAmount = 0m;
        Assert.False(vm.HasTxSellPreview);
    }

    [Fact]
    public async Task ConfirmAdd_Loan_ConvertsAnnualRatePercentToDecimal()
    {
        LoanTransactionRequest? captured = null;
        var loanMutation = new Mock<ILoanMutationWorkflowService>();
        loanMutation.Setup(s => s.RecordAsync(
                It.IsAny<LoanTransactionRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<LoanTransactionRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new LoanMutationResult(null, null));

        var vm = new AddAssetDialogViewModel(
            Mock.Of<IAddAssetWorkflowService>(),
            Mock.Of<IAccountUpsertWorkflowService>(),
            Mock.Of<ITransactionWorkflowService>(),
            Mock.Of<ICreditCardMutationWorkflowService>(),
            loanMutationWorkflow: loanMutation.Object);

        vm.AddAssetType = "loan";
        vm.AddLoanName = "台新 7y A";
        vm.AddLoanAmount = "1200000";
        vm.AddLoanAnnualRate = "2";
        vm.AddLoanTermMonths = "84";
        vm.AddLoanStartDate = new DateTime(2026, 6, 1);

        await vm.ConfirmAddCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.AddError);
        Assert.NotNull(captured);
        Assert.Equal(0.02m, captured!.AmortAnnualRate);
    }

    [Fact]
    public async Task ExecuteSellFromTxDialogAsync_PassesTradeDateToWorkflow()
    {
        SellWorkflowRequest? captured = null;
        var workflow = new Mock<ISellWorkflowService>();
        workflow.Setup(s => s.RecordAsync(
                It.IsAny<SellWorkflowRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<SellWorkflowRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new SellWorkflowResult(CreateSellTrade(), 9));
        var vm = new SellPanelViewModel(workflow.Object, new PortfolioSellPanelController());
        var tradeDate = new DateTime(2026, 5, 1, 16, 0, 0, DateTimeKind.Utc);

        var error = await vm.ExecuteSellFromTxDialogAsync(
            CreatePositionRow(),
            "120",
            tradeDate,
            null,
            false,
            1);

        Assert.Null(error);
        Assert.NotNull(captured);
        Assert.Equal(tradeDate, captured!.TradeDate);
    }

    [Fact]
    public async Task ExecuteSellFromTxDialogAsync_WhenWorkflowFails_ReturnsError()
    {
        var workflow = new Mock<ISellWorkflowService>();
        workflow.Setup(s => s.RecordAsync(
                It.IsAny<SellWorkflowRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));
        var vm = new SellPanelViewModel(workflow.Object, new PortfolioSellPanelController());

        var error = await vm.ExecuteSellFromTxDialogAsync(
            CreatePositionRow(),
            "120",
            new DateTime(2026, 5, 1, 16, 0, 0, DateTimeKind.Utc),
            null,
            false,
            1);

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Equal(error, vm.SellPanelError);
    }

    private static PortfolioRowViewModel CreatePositionRow() => new()
    {
        Id = Guid.NewGuid(),
        Symbol = "2330",
        Exchange = "TWSE",
        Name = "台積電",
        BuyPrice = 100m,
        Quantity = 10,
        CurrentPrice = 120m,
    };

    private static Trade CreateSellTrade() => new(
        Id: Guid.NewGuid(),
        Symbol: "2330",
        Exchange: "TWSE",
        Name: "台積電",
        Type: TradeType.Sell,
        TradeDate: DateTime.UtcNow,
        Price: 120m,
        Quantity: 1,
        RealizedPnl: 20m,
        RealizedPnlPct: 20m);
}
