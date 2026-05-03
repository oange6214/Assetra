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
        ObservableCollection<LiabilityRowViewModel>? liabilities = null,
        ICategoryRepository? categoryRepository = null,
        IAutoCategorizationRuleRepository? ruleRepository = null)
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
            Localize: (_, fallback) => fallback,
            CategoryRepository: categoryRepository,
            AutoCategorizationRuleRepository: ruleRepository);

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
    public void TxDate_FutureDate_ClampsToToday()
    {
        var vm = CreateVm();

        vm.TxDate = DateTime.Today.AddDays(3);

        Assert.Equal(DateTime.Today, vm.TxDate);
        Assert.Equal(DateTime.Today, vm.AddAssetDialog.AddBuyDate);
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

    [Fact]
    public async Task AutoCategorization_FiltersRulesByCurrentTransactionType()
    {
        var incomeCategoryId = Guid.NewGuid();
        var expenseCategoryId = Guid.NewGuid();
        var categoryRepo = new Mock<ICategoryRepository>();
        categoryRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ExpenseCategory(incomeCategoryId, "薪資", CategoryKind.Income, null, "💼", "#22C55E", 1, false),
                new ExpenseCategory(expenseCategoryId, "交通", CategoryKind.Expense, null, "🚇", "#3B82F6", 2, false),
            ]);
        var ruleRepo = new Mock<IAutoCategorizationRuleRepository>();
        ruleRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new AutoCategorizationRule(Guid.NewGuid(), "uber", incomeCategoryId, 0, true, false),
                new AutoCategorizationRule(Guid.NewGuid(), "uber", expenseCategoryId, 1, true, false),
            ]);
        var vm = CreateVm(
            categoryRepository: categoryRepo.Object,
            ruleRepository: ruleRepo.Object);
        await WaitForAsync(() => vm.IncomeCategories.Count == 1 && vm.ExpenseCategories.Count == 1);

        vm.TxType = "withdrawal";
        vm.TxNote = "uber trip";

        Assert.Equal(expenseCategoryId, vm.TxCategoryId);
        Assert.Same(vm.ExpenseCategories, vm.CashFlowCategories);

        vm.TxType = "income";

        Assert.Equal(incomeCategoryId, vm.TxCategoryId);

        vm.TxType = "deposit";

        Assert.Same(vm.IncomeCategories, vm.CashFlowCategories);
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
        Assert.Equal(new DateTime(2026, 6, 1), captured.TradeDate);
    }

    [Theory]
    [InlineData("crypto")]
    [InlineData("fund")]
    public async Task ConfirmAdd_ManualAssets_UseSelectedBuyDate(string assetType)
    {
        ManualAssetCreateRequest? captured = null;
        var addWorkflow = new Mock<IAddAssetWorkflowService>();
        addWorkflow.Setup(w => w.CreateManualAssetAsync(
                It.IsAny<ManualAssetCreateRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<ManualAssetCreateRequest, CancellationToken>((request, _) => captured = request)
            .Returns<ManualAssetCreateRequest, CancellationToken>((request, _) =>
                Task.FromResult(new ManualAssetCreateResult(
                    new PortfolioEntry(Guid.NewGuid(), request.Symbol, request.Exchange, request.AssetType, request.Name),
                    new PositionSnapshot(Guid.NewGuid(), request.Quantity, request.TotalCost, request.UnitPrice, 0m, request.AcquiredOn))));
        var vm = new AddAssetDialogViewModel(
            addWorkflow.Object,
            Mock.Of<IAccountUpsertWorkflowService>(),
            Mock.Of<ITransactionWorkflowService>(),
            Mock.Of<ICreditCardMutationWorkflowService>());
        var buyDate = new DateTime(2026, 4, 15);

        vm.AddAssetType = assetType;
        vm.AddBuyDate = buyDate;
        if (assetType == "crypto")
        {
            vm.AddCryptoSymbol = "btc";
            vm.AddCryptoQty = "0.5";
            vm.AddCryptoPrice = "2000000";
        }
        else
        {
            vm.AddName = "Global Fund";
            vm.AddCost = "10000";
        }

        await vm.ConfirmAddCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.AddError);
        Assert.NotNull(captured);
        Assert.Equal(DateOnly.FromDateTime(buyDate), captured!.AcquiredOn);
    }

    [Fact]
    public async Task ConfirmAdd_Stock_InvalidManualFee_ReturnsErrorWithoutRecording()
    {
        var addWorkflow = new Mock<IAddAssetWorkflowService>();
        addWorkflow.Setup(w => w.SearchSymbols(It.IsAny<string>(), It.IsAny<int>()))
            .Returns([]);
        addWorkflow.Setup(w => w.BuildBuyPreview(It.IsAny<BuyPreviewRequest>()))
            .Returns(new BuyPreviewResult(1000m, 0m, 1000m, 10m));
        var vm = new AddAssetDialogViewModel(
            addWorkflow.Object,
            Mock.Of<IAccountUpsertWorkflowService>(),
            Mock.Of<ITransactionWorkflowService>(),
            Mock.Of<ICreditCardMutationWorkflowService>())
        {
            GetTxFee = () => "abc",
        };

        vm.AddAssetType = "stock";
        vm.AddSymbol = "2330";
        vm.AddPrice = "100";
        vm.AddQuantity = "10";

        await vm.ConfirmAddCommand.ExecuteAsync(null);

        Assert.Contains("手續費", vm.AddError);
        addWorkflow.Verify(w => w.ExecuteStockBuyAsync(
            It.IsAny<StockBuyRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteSellFromTxDialogAsync_InvalidManualFee_ReturnsErrorWithoutRecording()
    {
        var workflow = new Mock<ISellWorkflowService>();
        var vm = new SellPanelViewModel(workflow.Object, new PortfolioSellPanelController())
        {
            GetTxFee = () => "-1",
        };

        var error = await vm.ExecuteSellFromTxDialogAsync(
            CreatePositionRow(),
            "120",
            new DateTime(2026, 5, 1, 16, 0, 0, DateTimeKind.Utc),
            null,
            false,
            1);

        Assert.NotNull(error);
        Assert.Contains("手續費", error);
        workflow.Verify(w => w.RecordAsync(
            It.IsAny<SellWorkflowRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
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

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 50; i++)
        {
            if (condition())
                return;

            await Task.Delay(20);
        }

        Assert.True(condition());
    }
}
