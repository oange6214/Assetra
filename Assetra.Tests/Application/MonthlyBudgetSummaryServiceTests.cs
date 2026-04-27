using Assetra.Application.Budget.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Application;

public class MonthlyBudgetSummaryServiceTests
{
    [Fact]
    public async Task BuildAsync_AggregatesIncomeAndExpense_ForGivenMonth()
    {
        var foodCat = Guid.NewGuid();
        var trades = new FakeTradeRepo();
        var budgets = new FakeBudgetRepo();
        var categories = new FakeCategoryRepo
        {
            Items = { new ExpenseCategory(foodCat, "餐飲", CategoryKind.Expense) }
        };

        trades.Store.Add(MakeIncome(new DateTime(2026, 4, 1), 50000m));
        trades.Store.Add(MakeWithdrawal(new DateTime(2026, 4, 5), 1200m, foodCat));
        trades.Store.Add(MakeWithdrawal(new DateTime(2026, 4, 18), 800m, foodCat));
        trades.Store.Add(MakeWithdrawal(new DateTime(2026, 3, 31), 999m, foodCat)); // outside month

        budgets.Store.Add(new Budget(Guid.NewGuid(), foodCat, BudgetMode.Monthly, 2026, 4, 5000m));
        budgets.Store.Add(new Budget(Guid.NewGuid(), null, BudgetMode.Monthly, 2026, 4, 30000m));

        var svc = new MonthlyBudgetSummaryService(trades, budgets, categories);
        var summary = await svc.BuildAsync(2026, 4);

        Assert.Equal(50000m, summary.TotalIncome);
        Assert.Equal(2000m, summary.TotalExpense);
        Assert.Equal(30000m, summary.TotalBudget);
        Assert.Equal(48000m, summary.NetCashFlow);
        var food = Assert.Single(summary.Categories);
        Assert.Equal(foodCat, food.CategoryId);
        Assert.Equal(2000m, food.Spent);
        Assert.Equal(5000m, food.BudgetAmount);
        Assert.False(food.IsOverBudget);
    }

    [Fact]
    public async Task BuildAsync_FlagsOverBudget()
    {
        var cat = Guid.NewGuid();
        var trades = new FakeTradeRepo();
        var budgets = new FakeBudgetRepo();
        var categories = new FakeCategoryRepo
        {
            Items = { new ExpenseCategory(cat, "娛樂", CategoryKind.Expense) }
        };

        trades.Store.Add(MakeWithdrawal(new DateTime(2026, 4, 10), 6000m, cat));
        budgets.Store.Add(new Budget(Guid.NewGuid(), cat, BudgetMode.Monthly, 2026, 4, 5000m));

        var svc = new MonthlyBudgetSummaryService(trades, budgets, categories);
        var summary = await svc.BuildAsync(2026, 4);

        var entertain = Assert.Single(summary.Categories);
        Assert.True(entertain.IsOverBudget);
        Assert.Equal(-1000m, entertain.Remaining);
    }

    [Fact]
    public async Task BuildAsync_LoanRepayInterest_CountsAsExpense()
    {
        var trades = new FakeTradeRepo();
        var budgets = new FakeBudgetRepo();
        var categories = new FakeCategoryRepo();

        trades.Store.Add(new Trade(
            Guid.NewGuid(), "", "", "貸款還款", TradeType.LoanRepay,
            new DateTime(2026, 4, 15), 0m, 1, null, null,
            CashAmount: 10000m, Principal: 9000m, InterestPaid: 1000m,
            LoanLabel: "信貸"));

        var svc = new MonthlyBudgetSummaryService(trades, budgets, categories);
        var summary = await svc.BuildAsync(2026, 4);

        Assert.Equal(1000m, summary.TotalExpense);
    }

    private static Trade MakeIncome(DateTime date, decimal amount) =>
        new(Guid.NewGuid(), "", "", "收入", TradeType.Income, date,
            0m, 1, null, null, CashAmount: amount);

    private static Trade MakeWithdrawal(DateTime date, decimal amount, Guid? categoryId) =>
        new(Guid.NewGuid(), "", "", "提款", TradeType.Withdrawal, date,
            0m, 1, null, null, CashAmount: amount, CategoryId: categoryId);

    private sealed class FakeTradeRepo : ITradeRepository
    {
        public List<Trade> Store { get; } = new();
        public Task<IReadOnlyList<Trade>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<Trade>>(Store.ToList());
        public Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid id) =>
            Task.FromResult<IReadOnlyList<Trade>>(Store.Where(t => t.CashAccountId == id).ToList());
        public Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string loanLabel) =>
            Task.FromResult<IReadOnlyList<Trade>>(Store.Where(t => t.LoanLabel == loanLabel).ToList());
        public Task<Trade?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<Trade?>(Store.FirstOrDefault(t => t.Id == id));
        public Task AddAsync(Trade t) { Store.Add(t); return Task.CompletedTask; }
        public Task UpdateAsync(Trade t) => Task.CompletedTask;
        public Task RemoveAsync(Guid id) => Task.CompletedTask;
        public Task RemoveChildrenAsync(Guid parentId) => Task.CompletedTask;
        public Task RemoveByAccountIdAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByLiabilityAsync(Guid? liabilityAssetId, string? loanLabel, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeBudgetRepo : IBudgetRepository
    {
        public List<Budget> Store { get; } = new();
        public Task<IReadOnlyList<Budget>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Budget>>(Store.ToList());
        public Task<Budget?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Store.FirstOrDefault(b => b.Id == id));
        public Task AddAsync(Budget b, CancellationToken ct = default) { Store.Add(b); return Task.CompletedTask; }
        public Task UpdateAsync(Budget b, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Budget>> GetByPeriodAsync(int year, int? month, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Budget>>(
                Store.Where(b => b.Year == year && b.Month == month).ToList());
    }

    private sealed class FakeCategoryRepo : ICategoryRepository
    {
        public List<ExpenseCategory> Items { get; } = new();
        public Task<IReadOnlyList<ExpenseCategory>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExpenseCategory>>(Items.ToList());
        public Task<ExpenseCategory?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Items.FirstOrDefault(c => c.Id == id));
        public Task AddAsync(ExpenseCategory c, CancellationToken ct = default) { Items.Add(c); return Task.CompletedTask; }
        public Task UpdateAsync(ExpenseCategory c, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> AnyAsync(CancellationToken ct = default) => Task.FromResult(Items.Count > 0);
    }
}
