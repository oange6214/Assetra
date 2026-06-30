using System.Collections.ObjectModel;
using Assetra.Application.Fx;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Features.Portfolio.SubViewModels;
using Assetra.WPF.Features.PortfolioGroups;
using Moq;
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
        IAutoCategorizationRuleRepository? ruleRepository = null,
        PortfolioGroupCatalog? groupCatalog = null,
        TransactionFxRateResolver? fxResolver = null,
        Func<CashAccountRowViewModel?>? getDefaultCashAccount = null,
        ISellWorkflowService? sellWorkflow = null)
    {
        var addWorkflow = new Mock<IAddAssetWorkflowService>();
        addWorkflow.Setup(w => w.BuildBuyPreview(It.IsAny<BuyPreviewRequest>()))
            .Returns(new BuyPreviewResult(0m, 0m, 0m, 0m));

        var addAsset = new AddAssetDialogViewModel(
            addWorkflow.Object,
            Mock.Of<IAccountUpsertWorkflowService>(),
            Mock.Of<ITransactionWorkflowService>(),
            Mock.Of<ICreditCardMutationWorkflowService>());
        var sellPanel = new SellPanelViewModel(
            sellWorkflow ?? Mock.Of<ISellWorkflowService>(),
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
            Trades: new ReadOnlyObservableCollection<TradeRowViewModel>(trades ?? new()),
            Positions: new ReadOnlyObservableCollection<PortfolioRowViewModel>(positions ?? new()),
            CashAccounts: new ReadOnlyObservableCollection<CashAccountRowViewModel>(cashAccounts ?? new()),
            Liabilities: new ReadOnlyObservableCollection<LiabilityRowViewModel>(liabilities ?? new()),
            AddAssetDialog: addAsset,
            SellPanel: sellPanel,
            GetDefaultCashAccount: getDefaultCashAccount ?? (() => null),
            LoadLoanScheduleAsync: _ => Task.CompletedTask,
            LoadLiabilitiesAsync: () => Task.CompletedTask,
            LoadPositionsAsync: () => Task.CompletedTask,
            LoadTradesAsync: () => Task.CompletedTask,
            ReloadAccountBalancesAsync: () => Task.CompletedTask,
            RebuildTotals: () => { },
            Localize: (_, fallback) => fallback,
            CategoryRepository: categoryRepository,
            AutoCategorizationRuleRepository: ruleRepository,
            GroupCatalog: groupCatalog,
            TransactionFxRateResolver: fxResolver);

        return new TransactionDialogViewModel(deps);
    }

    private sealed class FakePortfolioGroupRepo(IReadOnlyList<PortfolioGroup> groups) : IPortfolioGroupRepository
    {
        public Task<IReadOnlyList<PortfolioGroup>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult(groups);

        public Task<PortfolioGroup?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(groups.FirstOrDefault(g => g.Id == id));

        public Task AddAsync(PortfolioGroup group, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UpdateAsync(PortfolioGroup group, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(Guid id, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private static CashAccountRowViewModel MakeCashAccount(string name, string currency) =>
        new(new AssetItem(
            Guid.NewGuid(),
            name,
            FinancialType.Asset,
            null,
            currency,
            DateOnly.FromDateTime(DateTime.Today)),
            projectedBalance: 100_000m);

    private static PortfolioRowViewModel MakePosition(
        string symbol = "0056",
        string name = "元大高股息",
        string currency = "TWD") =>
        new()
        {
            Id = Guid.NewGuid(),
            Symbol = symbol,
            Exchange = currency == "TWD" ? "TWSE" : "NASDAQ",
            Name = name,
            Currency = currency,
            BuyPrice = 35m,
            Quantity = 15_000m,
            CurrentPrice = 46m,
        };

    private static LiabilityRowViewModel MakeLoanLiability(
        string name = "台新 7y",
        string currency = "TWD")
    {
        var asset = new AssetItem(
            Guid.NewGuid(),
            name,
            FinancialType.Liability,
            GroupId: null,
            currency,
            DateOnly.FromDateTime(DateTime.Today),
            LoanAnnualRate: 0.025m,
            LoanTermMonths: 84,
            LoanStartDate: DateOnly.FromDateTime(DateTime.Today.AddMonths(-3)));

        return new LiabilityRowViewModel(
            name,
            new LiabilitySnapshot(new Money(2_000_000m, currency), new Money(2_500_000m, currency)),
            asset);
    }

    private static LiabilityRowViewModel MakeCreditCardLiability(
        string name = "富邦 J 卡",
        string currency = "TWD")
    {
        var asset = new AssetItem(
            Guid.NewGuid(),
            name,
            FinancialType.Liability,
            GroupId: null,
            currency,
            DateOnly.FromDateTime(DateTime.Today),
            LiabilitySubtype: LiabilitySubtype.CreditCard,
            BillingDay: 5,
            DueDay: 20,
            CreditLimit: 80_000m,
            IssuerName: "Fubon");

        return new LiabilityRowViewModel(
            name,
            new LiabilitySnapshot(new Money(12_000m, currency), new Money(12_000m, currency)),
            asset);
    }

    [Fact]
    public void OpenTxDialog_ShowsDefaultCashAccountNameWhenDefaultAccountExists()
    {
        var defaultAccount = MakeCashAccount("富邦", "TWD");
        var cashAccounts = new ObservableCollection<CashAccountRowViewModel>
        {
            defaultAccount,
            MakeCashAccount("台新 Richart", "TWD"),
        };
        var vm = CreateVm(
            cashAccounts: cashAccounts,
            getDefaultCashAccount: () => defaultAccount);

        vm.OpenTxDialog();

        Assert.True(vm.TxUseCashAccount);
        Assert.Same(defaultAccount, vm.TxCashAccount);
        Assert.Equal("富邦", vm.TxCashAccountName);
        Assert.Equal("TWD", vm.Buy.CashAccountCurrency);
        Assert.Equal("TWD", vm.Buy.SettlementCurrency);
    }

    [Fact]
    public void OpenTxDialogForPosition_BuyPreselectsAssetAndLocksAssetSelector()
    {
        var position = MakePosition();
        var vm = CreateVm(positions: new ObservableCollection<PortfolioRowViewModel> { position });

        vm.OpenTxDialogForPosition(position, "buy");

        Assert.True(vm.IsTxDialogOpen);
        Assert.True(vm.IsAssetContextLocked);
        Assert.False(vm.ShowAssetSelector);
        Assert.Equal("buy", vm.TxType);
        Assert.NotNull(vm.SelectedAsset);
        Assert.Equal(position.Id, vm.SelectedAsset.Id);
        Assert.Equal("0056", vm.SelectedAsset.Symbol);
        Assert.Equal("0056", vm.AddAssetDialog.AddSymbol);
        Assert.Equal("TWD", vm.TxCurrency);
    }

    [Fact]
    public async Task OpenTxDialogForPosition_UsesPositionGroupWhenCatalogIsLoaded()
    {
        var groupId = Guid.NewGuid();
        var group = new PortfolioGroup(groupId, "長期投資");
        var catalog = new PortfolioGroupCatalog(new FakePortfolioGroupRepo([
            new PortfolioGroup(PortfolioGroup.DefaultId, "預設", IsSystem: true),
            group,
        ]));
        await catalog.RefreshAsync();
        var position = MakePosition();
        position.PortfolioGroupId = groupId;
        var vm = CreateVm(
            positions: new ObservableCollection<PortfolioRowViewModel> { position },
            groupCatalog: catalog);

        vm.OpenTxDialogForPosition(position, "buy");

        Assert.Equal(groupId, vm.SelectedPortfolioGroup?.Id);
    }

    [Fact]
    public void OpenTxDialogForPosition_CashDividendPreselectsDividendPositionAndHidesStockPicker()
    {
        var position = MakePosition();
        var vm = CreateVm(positions: new ObservableCollection<PortfolioRowViewModel> { position });

        vm.OpenTxDialogForPosition(position, "cashDiv");

        Assert.True(vm.IsAssetContextLocked);
        Assert.False(vm.ShowAssetSelector);
        Assert.False(vm.ShowDividendPositionPicker);
        Assert.Equal("cashDiv", vm.TxType);
        Assert.Same(position, vm.Div.Position);
        Assert.NotNull(vm.SelectedAsset);
        Assert.Equal(position.Id, vm.SelectedAsset.Id);
    }

    [Fact]
    public void OpenTxDialogForLiability_LoanRepayPreselectsLoanAssetAndLocksAssetSelector()
    {
        var liability = MakeLoanLiability();
        var vm = CreateVm(liabilities: new ObservableCollection<LiabilityRowViewModel> { liability });

        vm.OpenTxDialogForLiability(liability, "loanRepay");

        Assert.True(vm.IsTxDialogOpen);
        Assert.True(vm.IsAssetContextLocked);
        Assert.False(vm.ShowAssetSelector);
        Assert.Equal("loanRepay", vm.TxType);
        Assert.NotNull(vm.SelectedAsset);
        Assert.Equal(TxAssetKind.Liability, vm.SelectedAsset.Kind);
        Assert.Equal(liability.AssetId, vm.SelectedAsset.Id);
        Assert.Equal(liability.Label, vm.SelectedAsset.PrimaryName);
        Assert.Equal(liability.Label, vm.Loan.Label);
    }

    [Fact]
    public void OpenTxDialogForCashAccount_PreselectsAccountAndLocksAssetSelector()
    {
        var cash = MakeCashAccount("富邦", "TWD");
        var vm = CreateVm(cashAccounts: new ObservableCollection<CashAccountRowViewModel> { cash });

        vm.OpenTxDialogForCashAccount(cash, "deposit");

        Assert.True(vm.IsTxDialogOpen);
        Assert.True(vm.IsAssetContextLocked);
        Assert.False(vm.ShowAssetSelector);
        Assert.NotNull(vm.SelectedAsset);
        Assert.Equal(TxAssetKind.CashAccount, vm.SelectedAsset.Kind);
        Assert.Equal(cash.Id, vm.SelectedAsset.Id);
        Assert.Equal("deposit", vm.TxType);
        Assert.Same(cash, vm.TxCashAccount);
    }

    [Fact]
    public void OpenTxDialogForCashAccount_TransferLocksSourceAndKeepsTargetPickerAvailable()
    {
        var source = MakeCashAccount("台新 Richart", "TWD");
        var target = MakeCashAccount("富邦", "TWD");
        var vm = CreateVm(
            cashAccounts: new ObservableCollection<CashAccountRowViewModel> { source, target });

        vm.OpenTxDialogForCashAccount(source, "transfer");

        Assert.Equal("transfer", vm.TxType);
        Assert.True(vm.TxTypeIsTransfer);
        Assert.True(vm.IsAssetContextLocked);
        // Locking the asset picker only hides the unified selector; the transfer target
        // picker is gated by TxTypeIsTransfer and stays usable from the cash panel.
        Assert.False(vm.ShowAssetSelector);
        Assert.Equal(TxAssetKind.CashAccount, vm.SelectedAsset!.Kind);
        Assert.Equal(source.Id, vm.SelectedAsset.Id);
        Assert.Same(source, vm.TxCashAccount);
    }

    [Fact]
    public void OpenTxDialogForLoanSchedule_PrefillsScheduleEntryAndLocksLoanAsset()
    {
        var liability = MakeLoanLiability();
        var scheduleEntry = new LoanScheduleRowViewModel(new LoanScheduleEntry(
            Guid.NewGuid(),
            liability.AssetId!.Value,
            23,
            new DateOnly(2026, 5, 30),
            25_978m,
            22_833m,
            3_145m,
            1_000_000m,
            IsPaid: false,
            PaidAt: null,
            TradeId: null));
        liability.ReplaceSchedule([scheduleEntry]);
        liability.IsScheduleLoaded = true;
        var vm = CreateVm(liabilities: new ObservableCollection<LiabilityRowViewModel> { liability });

        vm.OpenTxDialogForLoanSchedule(liability, scheduleEntry);

        Assert.True(vm.IsTxDialogOpen);
        Assert.True(vm.IsAssetContextLocked);
        Assert.False(vm.ShowAssetSelector);
        Assert.Equal("loanRepay", vm.TxType);
        Assert.Equal(liability.AssetId, vm.SelectedAsset?.Id);
        Assert.Equal(liability.Label, vm.Loan.Label);
        Assert.Equal("22833", vm.Loan.Principal);
        Assert.Equal("3145", vm.Loan.InterestPaid);
        Assert.Equal(new DateTime(2026, 5, 30), vm.TxDate);
        Assert.Equal(scheduleEntry.Id, vm.TxLoanScheduleEntryId);
    }

    [Fact]
    public void OpenTxDialogForLiability_CreditCardPaymentPreselectsCardAndLocksAssetSelector()
    {
        var liability = MakeCreditCardLiability();
        var vm = CreateVm(liabilities: new ObservableCollection<LiabilityRowViewModel> { liability });

        vm.OpenTxDialogForLiability(liability, "creditCardPayment");

        Assert.True(vm.IsTxDialogOpen);
        Assert.True(vm.IsAssetContextLocked);
        Assert.False(vm.ShowAssetSelector);
        Assert.Equal("creditCardPayment", vm.TxType);
        Assert.NotNull(vm.SelectedAsset);
        Assert.Equal(TxAssetKind.Liability, vm.SelectedAsset.Kind);
        Assert.Equal(liability.AssetId, vm.SelectedAsset.Id);
        Assert.Equal(liability.Label, vm.SelectedAsset.PrimaryName);
        Assert.Same(liability, vm.CreditCard.Card);
    }

    [Fact]
    public void OpenTxDialog_GlobalEntryClearsPositionContext()
    {
        var position = MakePosition();
        var vm = CreateVm(positions: new ObservableCollection<PortfolioRowViewModel> { position });

        vm.OpenTxDialogForPosition(position, "buy");
        vm.OpenTxDialog();

        Assert.False(vm.IsAssetContextLocked);
        Assert.True(vm.ShowAssetSelector);
        Assert.True(vm.ShowDividendPositionPicker);
        Assert.Null(vm.SelectedAsset);
    }

    [Fact]
    public void EditTrade_TransferPreselectsSourceCashAccountAndHydratesTypePicker()
    {
        var source = MakeCashAccount("台新 Richart", "TWD");
        var target = MakeCashAccount("富邦", "TWD");
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(),
            Symbol: string.Empty,
            Exchange: string.Empty,
            Name: $"{source.Name} → {target.Name}",
            Type: TradeType.Transfer,
            TradeDate: new DateTime(2026, 5, 27),
            Price: 0m,
            Quantity: 0,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: 200_000m,
            CashAccountId: source.Id,
            ToCashAccountId: target.Id));
        var vm = CreateVm(
            trades: new ObservableCollection<TradeRowViewModel> { row },
            cashAccounts: new ObservableCollection<CashAccountRowViewModel> { source, target });

        vm.EditTradeCommand.Execute(row);

        Assert.Equal("transfer", vm.TxType);
        Assert.NotNull(vm.SelectedAsset);
        Assert.Equal(TxAssetKind.CashAccount, vm.SelectedAsset.Kind);
        Assert.Equal(source.Id, vm.SelectedAsset.Id);
        Assert.Same(source, vm.TxCashAccount);
        Assert.Same(target, vm.Transfer.Target);
        Assert.Equal("200000", vm.TxAmount);
        Assert.True(vm.CanSelectTxType);
        Assert.Contains(vm.AvailableTradeTypes, t => t.Key == "transfer");
    }

    [Theory]
    [InlineData(TradeType.Buy, "buy")]
    [InlineData(TradeType.CashDividend, "cashDiv")]
    [InlineData(TradeType.StockDividend, "stockDiv")]
    public void EditTrade_InvestmentRowsPreselectAssetAndHydrateTypePicker(
        TradeType tradeType,
        string expectedTxType)
    {
        var position = MakePosition();
        var cash = MakeCashAccount("富邦", "TWD");
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(),
            Symbol: position.Symbol,
            Exchange: position.Exchange,
            Name: position.Name,
            Type: tradeType,
            TradeDate: new DateTime(2026, 5, 27),
            Price: 35m,
            Quantity: tradeType == TradeType.StockDividend ? 100 : 10,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: tradeType == TradeType.CashDividend ? 500m : null,
            CashAccountId: tradeType == TradeType.CashDividend ? cash.Id : null,
            PortfolioEntryId: position.Id));
        var vm = CreateVm(
            trades: new ObservableCollection<TradeRowViewModel> { row },
            positions: new ObservableCollection<PortfolioRowViewModel> { position },
            cashAccounts: new ObservableCollection<CashAccountRowViewModel> { cash });

        vm.EditTradeCommand.Execute(row);

        Assert.Equal(expectedTxType, vm.TxType);
        Assert.NotNull(vm.SelectedAsset);
        Assert.Equal(position.Id, vm.SelectedAsset.Id);
        Assert.Contains(vm.AvailableTradeTypes, t => t.Key == expectedTxType);
    }

    // ── Q02: editing a trade whose asset is no longer in the live view ───────────
    // Closed lots are excluded from Positions by ShowClosedPositions (off by default), so editing a
    // Sell/Buy/StockDividend on such a lot used to leave SelectedAsset null (asset-subject
    // resolution searched only the live collections) — which ALSO blanked the type picker,
    // since AvailableTradeTypes is filtered by SelectedAsset.Kind. The fix synthesizes an
    // investment subject from the trade row and injects it into AvailableAssets so the
    // ComboBox can render it (SelectedItem ∈ ItemsSource) and the type picker re-populates.

    [Theory]
    [InlineData(TradeType.Sell, "sell")]
    [InlineData(TradeType.Buy, "buy")]
    [InlineData(TradeType.StockDividend, "stockDiv")]
    public void EditTrade_InvestmentRowWithClosedPosition_StillPreselectsAssetAndType(
        TradeType tradeType,
        string expectedTxType)
    {
        // Position deliberately omitted from `positions` (simulating a closed/empty lot),
        // but the trade still carries its Symbol + PortfolioEntryId link.
        var entryId = Guid.NewGuid();
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(),
            Symbol: "2330",
            Exchange: "TWSE",
            Name: "台積電",
            Type: tradeType,
            TradeDate: new DateTime(2026, 5, 27),
            Price: 600m,
            Quantity: tradeType == TradeType.StockDividend ? 100 : 10,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: tradeType == TradeType.Sell ? 6_000m : null,
            PortfolioEntryId: entryId));
        var vm = CreateVm(trades: new ObservableCollection<TradeRowViewModel> { row });

        vm.EditTradeCommand.Execute(row);

        Assert.Equal(expectedTxType, vm.TxType);
        Assert.NotNull(vm.SelectedAsset);
        // Synthesized subject is investment-kind with the row's symbol — NOT null, NOT a wrong kind.
        Assert.True(vm.SelectedAsset.Kind is TxAssetKind.Stock or TxAssetKind.Fund
            or TxAssetKind.Crypto or TxAssetKind.Metal or TxAssetKind.Bond);
        Assert.Equal("2330", vm.SelectedAsset.Symbol);
        // The synthesized subject must be in the picker's source so the ComboBox renders it.
        Assert.Contains(vm.AvailableAssets, a => ReferenceEquals(a, vm.SelectedAsset));
        // Type picker is hydrated (not blank) and contains the expected type.
        Assert.True(vm.CanSelectTxType);
        Assert.Contains(vm.AvailableTradeTypes, t => t.Key == expectedTxType);
    }

    [Fact]
    public void EditTrade_CashDividendWithClosedPosition_PreselectsInvestmentNotCashAccount()
    {
        // The trade has a CashAccountId (where the dividend was paid in), and the cash account
        // still exists — but the underlying position is closed and absent from `positions`.
        // The symbol lookup misses; resolution must NOT fall through to the CashAccount branch
        // (which would mis-resolve to a CashAccount-kind subject and blank the cashDiv type),
        // but instead synthesize the investment subject.
        var cash = MakeCashAccount("富邦", "TWD");
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(),
            Symbol: "0056",
            Exchange: "TWSE",
            Name: "元大高股息",
            Type: TradeType.CashDividend,
            TradeDate: new DateTime(2026, 5, 27),
            Price: 0m,
            Quantity: 10,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: 500m,
            CashAccountId: cash.Id,
            PortfolioEntryId: Guid.NewGuid()));
        var vm = CreateVm(
            trades: new ObservableCollection<TradeRowViewModel> { row },
            cashAccounts: new ObservableCollection<CashAccountRowViewModel> { cash });

        vm.EditTradeCommand.Execute(row);

        Assert.Equal("cashDiv", vm.TxType);
        Assert.NotNull(vm.SelectedAsset);
        Assert.NotEqual(TxAssetKind.CashAccount, vm.SelectedAsset.Kind);
        Assert.Equal("0056", vm.SelectedAsset.Symbol);
        Assert.Contains(vm.AvailableAssets, a => ReferenceEquals(a, vm.SelectedAsset));
        // Type picker not blank — contains the cashDiv key.
        Assert.True(vm.CanSelectTxType);
        Assert.Contains(vm.AvailableTradeTypes, t => t.Key == "cashDiv");
    }

    [Theory]
    [InlineData(TradeType.Deposit, "deposit")]
    [InlineData(TradeType.Withdrawal, "withdrawal")]
    [InlineData(TradeType.Income, "income")]
    public void EditTrade_CashFlowRowsPreselectCashAccountAndHydrateTypePicker(
        TradeType tradeType,
        string expectedTxType)
    {
        var account = MakeCashAccount("台新 Richart", "TWD");
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(),
            Symbol: string.Empty,
            Exchange: string.Empty,
            Name: account.Name,
            Type: tradeType,
            TradeDate: new DateTime(2026, 5, 27),
            Price: 0m,
            Quantity: 0,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: 1_000m,
            CashAccountId: account.Id));
        var vm = CreateVm(
            trades: new ObservableCollection<TradeRowViewModel> { row },
            cashAccounts: new ObservableCollection<CashAccountRowViewModel> { account });

        vm.EditTradeCommand.Execute(row);

        Assert.Equal(expectedTxType, vm.TxType);
        Assert.NotNull(vm.SelectedAsset);
        Assert.Equal(TxAssetKind.CashAccount, vm.SelectedAsset.Kind);
        Assert.Equal(account.Id, vm.SelectedAsset.Id);
        Assert.Contains(vm.AvailableTradeTypes, t => t.Key == expectedTxType);
    }

    [Theory]
    [InlineData(TradeType.LoanBorrow, "loanBorrow")]
    [InlineData(TradeType.LoanRepay, "loanRepay")]
    [InlineData(TradeType.CreditCardCharge, "creditCardCharge")]
    [InlineData(TradeType.CreditCardPayment, "creditCardPayment")]
    public void EditTrade_LiabilityRowsPreselectLiabilityAndHydrateTypePicker(
        TradeType tradeType,
        string expectedTxType)
    {
        var liability = tradeType is TradeType.LoanBorrow or TradeType.LoanRepay
            ? MakeLoanLiability("台新 7y")
            : MakeCreditCardLiability("富邦 J 卡");
        var cash = MakeCashAccount("富邦", "TWD");
        var row = new TradeRowViewModel(new Trade(
            Id: Guid.NewGuid(),
            Symbol: string.Empty,
            Exchange: string.Empty,
            Name: liability.Label,
            Type: tradeType,
            TradeDate: new DateTime(2026, 5, 27),
            Price: 0m,
            Quantity: 0,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: 1_000m,
            CashAccountId: cash.Id,
            LoanLabel: tradeType is TradeType.LoanBorrow or TradeType.LoanRepay ? liability.Label : null,
            LiabilityAssetId: liability.AssetId));
        var vm = CreateVm(
            trades: new ObservableCollection<TradeRowViewModel> { row },
            cashAccounts: new ObservableCollection<CashAccountRowViewModel> { cash },
            liabilities: new ObservableCollection<LiabilityRowViewModel> { liability });

        vm.EditTradeCommand.Execute(row);

        Assert.Equal(expectedTxType, vm.TxType);
        Assert.NotNull(vm.SelectedAsset);
        Assert.Equal(TxAssetKind.Liability, vm.SelectedAsset.Kind);
        Assert.Equal(liability.AssetId, vm.SelectedAsset.Id);
        Assert.Contains(vm.AvailableTradeTypes, t => t.Key == expectedTxType);
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
    public void TxDate_FutureDate_IsAllowedAndSyncsBuyDate()
    {
        var vm = CreateVm();
        var future = DateTime.Today.AddDays(3);

        vm.TxDate = future;

        Assert.Equal(future, vm.TxDate);
        Assert.Equal(future, vm.AddAssetDialog.AddBuyDate);
    }

    [Fact]
    public async Task FetchBuyFxRate_CrossCurrencyBuy_FillsRateMetadata()
    {
        var tradeDate = new DateOnly(2026, 5, 8);
        var history = new Mock<IFxRateHistoryService>();
        history
            .Setup(h => h.GetEntryAsync(
                tradeDate,
                "USD",
                "TWD",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FxRateHistoryEntry(
                tradeDate,
                "USD",
                "TWD",
                32.335m,
                "bot",
                DateTimeOffset.UtcNow));

        var vm = CreateVm(fxResolver: new TransactionFxRateResolver(history.Object));
        vm.TxType = "buy";
        vm.TxDate = tradeDate.ToDateTime(TimeOnly.MinValue);
        vm.Buy.InstrumentCurrency = "USD";
        vm.Buy.SettlementCurrency = "TWD";

        await vm.FetchBuyFxRateCommand.ExecuteAsync(null);

        Assert.Equal("32.335", vm.Buy.FxRate);
        Assert.Equal(tradeDate, vm.Buy.FxRateDate);
        Assert.Equal("bot", vm.Buy.FxSourceLabel);
        Assert.False(vm.Buy.IsFxManual);
        Assert.Equal(string.Empty, vm.Buy.FxFetchError);
    }

    [Fact]
    public void EditTrade_CrossCurrencyBuy_RestoresFxSettlementMetadata()
    {
        var tradeId = Guid.NewGuid();
        var tradeDate = new DateOnly(2026, 5, 8);
        var row = new TradeRowViewModel(new Trade(
            Id: tradeId,
            Symbol: "DRAM",
            Exchange: "NASDAQ",
            Name: "Roundhill Memory ETF",
            Type: TradeType.Buy,
            TradeDate: tradeDate.ToDateTime(TimeOnly.MinValue),
            Price: 50m,
            Quantity: 20,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: 32_250m,
            Commission: 0m,
            CommissionDiscount: 1m,
            PortfolioEntryId: Guid.NewGuid(),
            InstrumentCurrency: "USD",
            SettlementCurrency: "TWD",
            FxRate: 32.25m,
            FxRateDate: tradeDate,
            FxSource: "manual"));

        var trades = new ObservableCollection<TradeRowViewModel> { row };
        var vm = CreateVm(trades: trades);

        vm.EditTradeCommand.Execute(row);

        Assert.Equal(tradeId, vm.EditingTradeId);
        Assert.Equal("buy", vm.TxType);
        Assert.Equal("32.25", vm.Buy.FxRate);
        Assert.Equal("USD", vm.Buy.InstrumentCurrency);
        Assert.Equal("TWD", vm.Buy.SettlementCurrency);
        Assert.Equal(tradeDate, vm.Buy.FxRateDate);
        Assert.Equal("manual", vm.Buy.FxSourceLabel);
        Assert.True(vm.Buy.IsFxManual);
        Assert.Equal("32250", vm.Buy.ActualCashAmount);
    }

    [Fact]
    public void BuyCashSettlement_UsesSelectedCashAccountCurrencyNotTradeCurrencyAssumption()
    {
        var usd = MakeCashAccount("IB", "USD");
        var twd = MakeCashAccount("台新", "TWD");
        var vm = CreateVm(cashAccounts: new ObservableCollection<CashAccountRowViewModel> { usd, twd });

        vm.TxType = "buy";
        vm.TxCurrency = "USD";
        vm.TxUseCashAccount = true;

        vm.TxCashAccount = usd;

        Assert.Equal("USD", vm.Buy.InstrumentCurrency);
        Assert.Equal("USD", vm.Buy.CashAccountCurrency);
        Assert.Equal("USD", vm.Buy.SettlementCurrency);
        Assert.False(vm.Buy.IsCrossCurrency);
        Assert.False(vm.CanFetchBuyFxRate);

        vm.TxCashAccount = twd;

        Assert.Equal("USD", vm.Buy.InstrumentCurrency);
        Assert.Equal("TWD", vm.Buy.CashAccountCurrency);
        Assert.Equal("TWD", vm.Buy.SettlementCurrency);
        Assert.True(vm.Buy.IsCrossCurrency);
        Assert.Equal("USD → TWD", vm.Buy.SettlementPairDisplay);
    }

    [Fact]
    public void BuySelectedUsdAsset_KeepsInstrumentCurrencySeparateFromDebitCurrency()
    {
        var twd = MakeCashAccount("富邦", "TWD");
        var position = new PortfolioRowViewModel
        {
            Id = Guid.NewGuid(),
            Symbol = "DRAM",
            Exchange = "NASDAQ",
            Name = "Roundhill Memory ETF",
            Currency = "USD",
            BuyPrice = 50m,
            Quantity = 20,
            CurrentPrice = 51m,
        };
        var vm = CreateVm(
            positions: new ObservableCollection<PortfolioRowViewModel> { position },
            cashAccounts: new ObservableCollection<CashAccountRowViewModel> { twd });

        vm.TxType = "buy";
        vm.SelectedAsset = vm.AvailableAssets.Single(a => a.Symbol == "DRAM");
        vm.TxCashAccount = twd;

        Assert.Equal("USD", vm.Buy.InstrumentCurrency);
        Assert.Equal("TWD", vm.Buy.CashAccountCurrency);
        Assert.Equal("USD → TWD", vm.Buy.SettlementPairDisplay);

        vm.TxCurrency = "TWD";

        Assert.Equal("USD", vm.TxCurrency);
        Assert.Equal("USD", vm.Buy.InstrumentCurrency);
        Assert.Equal("TWD", vm.Buy.CashAccountCurrency);
        Assert.True(vm.Buy.IsCrossCurrency);
        Assert.Equal("USD → TWD", vm.Buy.SettlementPairDisplay);
    }

    [Fact]
    public void BuySettlementInputMode_DefaultsToStatementAndCanSwitchToFxEstimate()
    {
        var vm = CreateVm();

        Assert.Equal("statement", vm.Buy.SettlementInputMode);
        Assert.True(vm.Buy.IsStatementSettlementMode);
        Assert.False(vm.Buy.IsFxSettlementMode);

        vm.Buy.SettlementInputMode = "fx";

        Assert.False(vm.Buy.IsStatementSettlementMode);
        Assert.True(vm.Buy.IsFxSettlementMode);
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
    public void TxTypeIsWithdrawal_TrueOnlyForWithdrawal_GatesCategoryVisibility()
    {
        // WHY: CashFlowTxForm 的分類欄綁 TxTypeIsWithdrawal 控制顯隱 —— 只有「提款」（支出）顯示
        // 支出分類；「存入」是把外部資金搬入、非收入非支出（與轉帳同性質），不顯示分類。
        var vm = CreateVm();
        vm.TxType = "withdrawal";
        Assert.True(vm.TxTypeIsWithdrawal);
        vm.TxType = "deposit";
        Assert.False(vm.TxTypeIsWithdrawal);
        vm.TxType = "income";
        Assert.False(vm.TxTypeIsWithdrawal);
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
        vm.Sell.Quantity = "1.5";
        Assert.NotEqual(string.Empty, vm.Sell.QuantityError);
    }

    [Fact]
    public void TxSellQuantity_PositiveInt_ClearsError()
    {
        var vm = CreateVm();
        vm.Sell.Quantity = "abc";
        Assert.NotEqual(string.Empty, vm.Sell.QuantityError);
        vm.Sell.Quantity = "1000";
        Assert.Equal(string.Empty, vm.Sell.QuantityError);
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
        // Source-of-truth pass (commit 6836323): AreEconomicFieldsEditable now follows
        // IsEditingMetaOnly, not just IsEditMode. With a Guid not backed by an actual
        // TradeRowViewModel in the Trades collection, IsEditingMetaOnly is false (no
        // matching row to classify), so economic fields stay editable. The meta-only
        // lockdown lives in EditTrade_*-named integration tests that prime real rows.
        var vm = CreateVm();
        Assert.False(vm.IsEditMode);
        Assert.True(vm.AreEconomicFieldsEditable);

        vm.EditingTradeId = Guid.NewGuid();
        Assert.True(vm.IsEditMode);
        Assert.True(vm.AreEconomicFieldsEditable);

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

    // ── Sell preview (Sell.HasPreview gating) ───────────────────────────────

    [Fact]
    public void HasTxSellPreview_FalseUntilGrossAmountSet()
    {
        var vm = CreateVm();
        Assert.False(vm.Sell.HasPreview);
        vm.Sell.GrossAmount = 100m;
        Assert.True(vm.Sell.HasPreview);
        vm.Sell.GrossAmount = 0m;
        Assert.False(vm.Sell.HasPreview);
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
            // Migrated from GetTxFee Func to typed IBuyExecutionContext snapshot.
            BuyContext = new StaticBuyContext(txFee: "abc"),
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
    public async Task ConfirmAdd_Stock_InvalidActualCashAmount_ReturnsErrorWithoutRecording()
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
            BuyContext = new StaticBuyContext(actualCashAmount: "abc"),
        };

        vm.AddAssetType = "stock";
        vm.AddSymbol = "AAPL";
        vm.AddPrice = "100";
        vm.AddQuantity = "10";

        await vm.ConfirmAddCommand.ExecuteAsync(null);

        Assert.Contains("實際扣款", vm.AddError);
        addWorkflow.Verify(w => w.ExecuteStockBuyAsync(
            It.IsAny<StockBuyRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmAdd_Stock_CrossCurrencyStatementModeRequiresActualCashAmount()
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
            BuyContext = new StaticBuyContext(
                cashAccountId: Guid.NewGuid(),
                cashAccountCurrency: "TWD",
                useCashAccount: true,
                settlementInputMode: "statement",
                fxRate: "32.5"),
        };

        vm.AddAssetType = "stock";
        vm.AddSymbol = "AAPL";
        vm.AddSymbolCurrency = "USD";
        vm.AddPrice = "100";
        vm.AddQuantity = "10";

        await vm.ConfirmAddCommand.ExecuteAsync(null);

        Assert.Contains("實際扣款", vm.AddError);
        addWorkflow.Verify(w => w.ExecuteStockBuyAsync(
            It.IsAny<StockBuyRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmAdd_Stock_CrossCurrencyFxModeAcceptsFxWithoutActualCashAmount()
    {
        StockBuyRequest? captured = null;
        var addWorkflow = new Mock<IAddAssetWorkflowService>();
        addWorkflow.Setup(w => w.SearchSymbols(It.IsAny<string>(), It.IsAny<int>()))
            .Returns([]);
        addWorkflow.Setup(w => w.BuildBuyPreview(It.IsAny<BuyPreviewRequest>()))
            .Returns(new BuyPreviewResult(1000m, 0m, 1000m, 10m));
        addWorkflow
            .Setup(w => w.ExecuteStockBuyAsync(It.IsAny<StockBuyRequest>(), It.IsAny<CancellationToken>()))
            .Callback<StockBuyRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new StockBuyResult(
                new PortfolioEntry(Guid.NewGuid(), "AAPL", "NASDAQ", Currency: "USD"),
                Commission: 0m,
                CommissionDiscountUsed: 1m,
                CostPerShare: 100m));

        var vm = new AddAssetDialogViewModel(
            addWorkflow.Object,
            Mock.Of<IAccountUpsertWorkflowService>(),
            Mock.Of<ITransactionWorkflowService>(),
            Mock.Of<ICreditCardMutationWorkflowService>())
        {
            BuyContext = new StaticBuyContext(
                cashAccountId: Guid.NewGuid(),
                cashAccountCurrency: "TWD",
                useCashAccount: true,
                settlementInputMode: "fx",
                fxRate: "32.5"),
        };

        vm.AddAssetType = "stock";
        vm.AddSymbol = "AAPL";
        vm.AddSymbolCurrency = "USD";
        vm.AddPrice = "100";
        vm.AddQuantity = "10";

        await vm.ConfirmAddCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.AddError);
        Assert.NotNull(captured);
        Assert.Equal(32.5m, captured!.FxRate);
        Assert.Equal(32_500m, captured.ActualCashAmount);
        Assert.Equal("TWD", captured.SettlementCurrency);
    }

    [Fact]
    public async Task ConfirmAdd_Stock_CrossCurrencyStatementMode_DerivesPriceFromActualCashUsingResolvedFxRate()
    {
        // 依帳戶明細 (statement) 模式：使用者只填「帳戶扣款金額」+ 股數、清空每股價，
        // 仍應用「已解析匯率」(自動依交易日抓回或手填) 反推單價，而非卡在「跨幣別反推單價需要匯率」。
        // 迴歸防護：先前 Mode-C 用 mode-gated 的 fxRate（statement 模式一律 null）→ 一律報錯、買入無法成立。
        StockBuyRequest? captured = null;
        var addWorkflow = new Mock<IAddAssetWorkflowService>();
        addWorkflow.Setup(w => w.SearchSymbols(It.IsAny<string>(), It.IsAny<int>()))
            .Returns([]);
        addWorkflow.Setup(w => w.BuildBuyPreview(It.IsAny<BuyPreviewRequest>()))
            .Returns(new BuyPreviewResult(1000m, 0m, 1000m, 100m));
        addWorkflow
            .Setup(w => w.ExecuteStockBuyAsync(It.IsAny<StockBuyRequest>(), It.IsAny<CancellationToken>()))
            .Callback<StockBuyRequest, CancellationToken>((request, _) => captured = request)
            .ReturnsAsync(new StockBuyResult(
                new PortfolioEntry(Guid.NewGuid(), "AAPL", "NASDAQ", Currency: "USD"),
                Commission: 0m,
                CommissionDiscountUsed: 1m,
                CostPerShare: 100m));

        var vm = new AddAssetDialogViewModel(
            addWorkflow.Object,
            Mock.Of<IAccountUpsertWorkflowService>(),
            Mock.Of<ITransactionWorkflowService>(),
            Mock.Of<ICreditCardMutationWorkflowService>())
        {
            BuyContext = new StaticBuyContext(
                cashAccountId: Guid.NewGuid(),
                cashAccountCurrency: "TWD",
                useCashAccount: true,
                settlementInputMode: "statement",
                actualCashAmount: "32500",
                fxRate: "32.5"),
        };

        vm.AddAssetType = "stock";
        vm.AddSymbol = "AAPL";
        vm.AddSymbolCurrency = "USD";
        vm.AddPrice = string.Empty;   // 清空每股價，期望由扣款金額 ÷ 股數 ÷ 匯率 反推
        vm.AddQuantity = "10";

        await vm.ConfirmAddCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.AddError);   // 不再卡「需要匯率」
        Assert.NotNull(captured);
        Assert.Equal(100m, captured!.Price);        // 32500 / 10 / 32.5 = 100
    }

    [Fact]
    public async Task ConfirmAdd_Stock_CrossCurrencyTypedSymbolUsesDirectoryCurrency()
    {
        var addWorkflow = new Mock<IAddAssetWorkflowService>();
        addWorkflow.Setup(w => w.SearchSymbols(It.IsAny<string>(), It.IsAny<int>()))
            .Returns([new StockSearchResult("AAPL", "Apple Inc.", "NASDAQ", Currency: "USD")]);
        addWorkflow.Setup(w => w.BuildBuyPreview(It.IsAny<BuyPreviewRequest>()))
            .Returns(new BuyPreviewResult(1000m, 0m, 1000m, 10m));
        var vm = new AddAssetDialogViewModel(
            addWorkflow.Object,
            Mock.Of<IAccountUpsertWorkflowService>(),
            Mock.Of<ITransactionWorkflowService>(),
            Mock.Of<ICreditCardMutationWorkflowService>())
        {
            BuyContext = new StaticBuyContext(
                cashAccountId: Guid.NewGuid(),
                cashAccountCurrency: "TWD",
                useCashAccount: true),
        };

        vm.AddAssetType = "stock";
        vm.AddSymbol = "AAPL";
        vm.AddPrice = "100";
        vm.AddQuantity = "10";

        await vm.ConfirmAddCommand.ExecuteAsync(null);

        Assert.Contains("跨幣別", vm.AddError);
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

    [Fact]
    public async Task ConfirmSellTx_TotalMode_DerivesUnitPriceFromTotalProceeds()
    {
        // 賣出「成交總額」模式採 GROSS：成交總額 1000 ÷ 股數 10 ⇒ 單價 100。
        // 鎖定 ConfirmSellTxAsync 會「反推單價」餵給賣出工作流，而非沿用（總額模式下為空的）單價欄位 TxAmount。
        // 此測試會在 GROSS 反推邏輯被改壞（例如誤用淨額、或除錯股數）時失敗 — 對齊 Rule 9「測意圖」。
        SellWorkflowRequest? captured = null;
        var workflow = new Mock<ISellWorkflowService>();
        workflow.Setup(s => s.RecordAsync(
                It.IsAny<SellWorkflowRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<SellWorkflowRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(new SellWorkflowResult(CreateSellTrade(), 9));

        var position = MakePosition();   // TWD、持倉 15,000 股
        var vm = CreateVm(
            positions: new ObservableCollection<PortfolioRowViewModel> { position },
            sellWorkflow: workflow.Object);

        vm.TxType = "sell";
        vm.Sell.Position = position;
        vm.Sell.Quantity = "10";
        vm.Sell.PriceMode = "total";
        vm.Sell.TotalProceeds = "1000";   // 故意不填 TxAmount（單價欄）

        await vm.ConfirmTxCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.TxError);
        Assert.NotNull(captured);
        Assert.Equal(100m, captured!.SellPrice);
        Assert.Equal(10, captured.SellQuantity);
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
