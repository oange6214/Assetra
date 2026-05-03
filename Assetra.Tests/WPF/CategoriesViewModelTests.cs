using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Features.Categories;
using Assetra.WPF.Infrastructure;
using Xunit;

namespace Assetra.Tests.WPF;

public sealed class CategoriesViewModelTests
{
    [Fact]
    public async Task DeleteAsync_BlocksWhenCategoryIsReferencedByBudgetOrRule()
    {
        var categoryId = Guid.NewGuid();
        var categoryRepo = new FakeCategoryRepo(
        [
            new ExpenseCategory(
                categoryId,
                "飲食",
                CategoryKind.Expense,
                null,
                "🍜",
                "#FF8800",
                1,
                false)
        ]);
        var ruleRepo = new FakeRuleRepo(
        [
            new AutoCategorizationRule(
                Guid.NewGuid(),
                "uber",
                categoryId,
                0,
                true,
                false)
        ]);
        var budgetRepo = new FakeBudgetRepo(
        [
            new Budget(
                Guid.NewGuid(),
                categoryId,
                BudgetMode.Monthly,
                2026,
                4,
                5000m,
                "TWD",
                null)
        ]);
        var snackbar = new FakeSnackbarService();

        var vm = new CategoriesViewModel(
            categoryRepo,
            ruleRepo,
            budgetRepo,
            new FakeTradeRepo(),
            new FakeRecurringRepo(),
            new FakePendingRepo(),
            new BudgetRefreshNotifier(),
            snackbar,
            new FakeLocalizationService());

        await vm.LoadAsync();
        var row = Assert.Single(vm.Categories);

        await vm.DeleteCommand.ExecuteAsync(row);

        Assert.Single(vm.Categories);
        Assert.Empty(categoryRepo.RemovedIds);
        Assert.Equal(
            "分類「飲食」仍被以下資料使用：1 筆預算、1 筆自動分類規則，請先解除關聯後再刪除",
            snackbar.LastWarning);
    }

    [Fact]
    public async Task DeleteAsync_BlocksWhenCategoryIsReferencedByTrade()
    {
        var categoryId = Guid.NewGuid();
        var categoryRepo = new FakeCategoryRepo(
        [
            new ExpenseCategory(categoryId, "飲食", CategoryKind.Expense, null, "🍜", "#FF8800", 1, false)
        ]);
        var snackbar = new FakeSnackbarService();
        var tradeRepo = new FakeTradeRepo
        {
            Store =
            [
                new Trade(
                    Id: Guid.NewGuid(),
                    Symbol: string.Empty,
                    Exchange: string.Empty,
                    Name: string.Empty,
                    Type: TradeType.Withdrawal,
                    TradeDate: DateTime.Today,
                    Price: 0m,
                    Quantity: 1,
                    RealizedPnl: null,
                    RealizedPnlPct: null,
                    CashAmount: 150m,
                    CategoryId: categoryId)
            ]
        };

        var vm = new CategoriesViewModel(
            categoryRepo,
            new FakeRuleRepo([]),
            new FakeBudgetRepo([]),
            tradeRepo,
            new FakeRecurringRepo(),
            new FakePendingRepo(),
            new BudgetRefreshNotifier(),
            snackbar,
            new FakeLocalizationService());

        await vm.LoadAsync();
        var row = Assert.Single(vm.Categories);

        await vm.DeleteCommand.ExecuteAsync(row);

        Assert.Single(vm.Categories);
        Assert.Empty(categoryRepo.RemovedIds);
        Assert.Equal(
            "分類「飲食」仍被以下資料使用：1 筆交易，請先解除關聯後再刪除",
            snackbar.LastWarning);
    }

    [Fact]
    public async Task ToggleArchiveAsync_BlocksRestoreWhenActiveDuplicateExists()
    {
        var archivedId = Guid.NewGuid();
        var activeId = Guid.NewGuid();
        var categoryRepo = new FakeCategoryRepo(
        [
            new ExpenseCategory(archivedId, "飲食", CategoryKind.Expense, null, "🍜", "#FF8800", 1, true),
            new ExpenseCategory(activeId, "飲食", CategoryKind.Expense, null, "🍱", "#F59E0B", 2, false),
        ]);
        var snackbar = new FakeSnackbarService();
        var vm = new CategoriesViewModel(
            categoryRepo,
            new FakeRuleRepo([]),
            new FakeBudgetRepo([]),
            new FakeTradeRepo(),
            new FakeRecurringRepo(),
            new FakePendingRepo(),
            new BudgetRefreshNotifier(),
            snackbar,
            new FakeLocalizationService());

        await vm.LoadAsync();
        var archived = vm.Categories.Single(c => c.Id == archivedId);

        await vm.ToggleArchiveCommand.ExecuteAsync(archived);

        Assert.True(archived.IsArchived);
        Assert.Equal("已存在同名分類", snackbar.LastWarning);
    }

    [Fact]
    public async Task SaveEditAsync_RefreshesBudgetCategoryDisplay()
    {
        var categoryId = Guid.NewGuid();
        var categoryRepo = new FakeCategoryRepo(
        [
            new ExpenseCategory(categoryId, "飲食", CategoryKind.Expense, null, "🍜", "#FF8800", 1, false)
        ]);
        var budgetRepo = new FakeBudgetRepo(
        [
            new Budget(Guid.NewGuid(), categoryId, BudgetMode.Monthly, 2026, 4, 5000m)
        ]);
        var vm = new CategoriesViewModel(
            categoryRepo,
            new FakeRuleRepo([]),
            budgetRepo,
            new FakeTradeRepo(),
            new FakeRecurringRepo(),
            new FakePendingRepo(),
            new BudgetRefreshNotifier(),
            new FakeSnackbarService(),
            new FakeLocalizationService());

        await vm.LoadAsync();
        var row = vm.Categories.Single();
        var budget = vm.Budgets.Single();
        Assert.Equal("🍜 飲食", budget.CategoryDisplay);

        row.EnterEditMode();
        row.EditName = "餐飲";
        row.EditIcon = "🍱";
        await vm.SaveEditCommand.ExecuteAsync(row);

        Assert.Equal("🍱 餐飲", budget.CategoryDisplay);
    }

    private sealed class FakeCategoryRepo(List<ExpenseCategory> seed) : ICategoryRepository
    {
        private readonly List<ExpenseCategory> _store = [..seed];
        public List<Guid> RemovedIds { get; } = [];

        public Task<IReadOnlyList<ExpenseCategory>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExpenseCategory>>(_store.ToList());

        public Task<ExpenseCategory?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_store.FirstOrDefault(x => x.Id == id));

        public Task AddAsync(ExpenseCategory category, CancellationToken ct = default)
        {
            _store.Add(category);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ExpenseCategory category, CancellationToken ct = default)
        {
            var index = _store.FindIndex(x => x.Id == category.Id);
            if (index >= 0) _store[index] = category;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(Guid id, CancellationToken ct = default)
        {
            RemovedIds.Add(id);
            _store.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }

        public Task<bool> AnyAsync(CancellationToken ct = default) =>
            Task.FromResult(_store.Count > 0);
    }

    private sealed class FakeRuleRepo(List<AutoCategorizationRule> seed) : IAutoCategorizationRuleRepository
    {
        private readonly List<AutoCategorizationRule> _store = [..seed];

        public Task<IReadOnlyList<AutoCategorizationRule>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AutoCategorizationRule>>(_store.ToList());

        public Task<AutoCategorizationRule?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_store.FirstOrDefault(x => x.Id == id));

        public Task AddAsync(AutoCategorizationRule rule, CancellationToken ct = default)
        {
            _store.Add(rule);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(AutoCategorizationRule rule, CancellationToken ct = default)
        {
            var index = _store.FindIndex(x => x.Id == rule.Id);
            if (index >= 0) _store[index] = rule;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(Guid id, CancellationToken ct = default)
        {
            _store.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBudgetRepo(List<Budget> seed) : IBudgetRepository
    {
        private readonly List<Budget> _store = [..seed];

        public Task<IReadOnlyList<Budget>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Budget>>(_store.ToList());

        public Task<Budget?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_store.FirstOrDefault(x => x.Id == id));

        public Task AddAsync(Budget budget, CancellationToken ct = default)
        {
            _store.Add(budget);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Budget budget, CancellationToken ct = default)
        {
            var index = _store.FindIndex(x => x.Id == budget.Id);
            if (index >= 0) _store[index] = budget;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(Guid id, CancellationToken ct = default)
        {
            _store.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Budget>> GetByPeriodAsync(int year, int? month, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Budget>>(
                _store.Where(x => x.Year == year && x.Month == month).ToList());
    }

    private sealed class FakeTradeRepo : ITradeRepository
    {
        public List<Trade> Store { get; set; } = [];

        public Task<IReadOnlyList<Trade>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Trade>>(Store.ToList());
        public Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid cashAccountId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string loanLabel, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task<Trade?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Trade?>(null);
        public Task AddAsync(Trade trade, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Trade trade, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveChildrenAsync(Guid parentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByAccountIdAsync(Guid accountId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByLiabilityAsync(Guid? liabilityAssetId, string? loanLabel, CancellationToken ct = default) => Task.CompletedTask;
        public Task ApplyAtomicAsync(IReadOnlyList<TradeMutation> mutations, CancellationToken ct = default)
        {
            foreach (var m in mutations)
            {
                switch (m)
                {
                    case AddTradeMutation add: Store.Add(add.Trade); break;
                    case RemoveTradeMutation rem: Store.RemoveAll(t => t.Id == rem.Id); break;
                }
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRecurringRepo : IRecurringTransactionRepository
    {
        public Task<IReadOnlyList<RecurringTransaction>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RecurringTransaction>>([]);

        public Task<RecurringTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<RecurringTransaction?>(null);

        public Task<IReadOnlyList<RecurringTransaction>> GetActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RecurringTransaction>>([]);

        public Task AddAsync(RecurringTransaction recurring, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(RecurringTransaction recurring, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakePendingRepo : IPendingRecurringEntryRepository
    {
        public Task<IReadOnlyList<PendingRecurringEntry>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PendingRecurringEntry>>([]);

        public Task<IReadOnlyList<PendingRecurringEntry>> GetByStatusAsync(PendingStatus status, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PendingRecurringEntry>>([]);

        public Task<PendingRecurringEntry?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<PendingRecurringEntry?>(null);

        public Task AddAsync(PendingRecurringEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(PendingRecurringEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByRecurringSourceAsync(Guid recurringSourceId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeSnackbarService : ISnackbarService
    {
        public string? LastWarning { get; private set; }

        public void Show(string message, SnackbarKind kind = SnackbarKind.Info) { }
        public void Success(string message) { }
        public void Warning(string message) => LastWarning = message;
        public void Error(string message) { }
    }

    private sealed class FakeLocalizationService : ILocalizationService
    {
        public string CurrentLanguage => "zh-TW";
        public event EventHandler? LanguageChanged;
        public string Get(string key, string fallback = "") => fallback;
        public void SetLanguage(string languageCode) => LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
