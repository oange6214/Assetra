using Assetra.Application.Budget.Services;
using Assetra.Application.Reports.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Application;

public class MonthEndReportServiceTests
{
    [Fact]
    public async Task BuildAsync_ProducesCurrentAndPrevious_WithDeltas()
    {
        var foodCat = Guid.NewGuid();
        var trades = new FakeTradeRepo();
        var budgets = new FakeBudgetRepo();
        var categories = new FakeCategoryRepo
        {
            Items = { new ExpenseCategory(foodCat, "餐飲", CategoryKind.Expense) },
        };
        var recurring = new FakeRecurringRepo();

        // March (previous) — income 40k, expense 1500
        trades.Store.Add(MakeIncome(new DateTime(2026, 3, 1), 40000m));
        trades.Store.Add(MakeWithdrawal(new DateTime(2026, 3, 10), 1500m, foodCat));

        // April (current) — income 50k, expense 2000
        trades.Store.Add(MakeIncome(new DateTime(2026, 4, 1), 50000m));
        trades.Store.Add(MakeWithdrawal(new DateTime(2026, 4, 5), 2000m, foodCat));

        budgets.Store.Add(new Budget(Guid.NewGuid(), foodCat, BudgetMode.Monthly, 2026, 4, 1500m));

        var summary = new MonthlyBudgetSummaryService(trades, budgets, categories);
        var svc = new MonthEndReportService(summary, recurring);

        var report = await svc.BuildAsync(2026, 4);

        Assert.Equal(50000m, report.Current.TotalIncome);
        Assert.NotNull(report.Previous);
        Assert.Equal(40000m, report.Previous!.TotalIncome);
        Assert.Equal(10000m, report.IncomeDelta);
        Assert.Equal(500m, report.ExpenseDelta);
        Assert.Single(report.OverBudgetCategories);
        Assert.True(report.HasAlerts);
        Assert.True(report.SavingsRate > 0);
    }

    [Fact]
    public async Task BuildAsync_IncludesUpcomingRecurring_Within14Days()
    {
        var trades = new FakeTradeRepo();
        var budgets = new FakeBudgetRepo();
        var categories = new FakeCategoryRepo();
        var recurring = new FakeRecurringRepo();

        var today = DateTime.Today;
        recurring.Store.Add(MakeRecurring("Soon",  today.AddDays(3)));
        recurring.Store.Add(MakeRecurring("Later", today.AddDays(20)));
        recurring.Store.Add(MakeRecurring("Past",  today.AddDays(-1)));

        var summary = new MonthlyBudgetSummaryService(trades, budgets, categories);
        var svc = new MonthEndReportService(summary, recurring);

        var report = await svc.BuildAsync(today.Year, today.Month);

        Assert.Single(report.Upcoming);
        Assert.Equal("Soon", report.Upcoming[0].Name);
    }

    private static Trade MakeIncome(DateTime when, decimal amount) => new(
        Guid.NewGuid(), string.Empty, string.Empty, "income",
        TradeType.Income, when, 0m, 1, null, null,
        CashAmount: amount);

    private static Trade MakeWithdrawal(DateTime when, decimal amount, Guid categoryId) => new(
        Guid.NewGuid(), string.Empty, string.Empty, "expense",
        TradeType.Withdrawal, when, 0m, 1, null, null,
        CashAmount: amount, CategoryId: categoryId);

    private static RecurringTransaction MakeRecurring(string name, DateTime nextDue) => new(
        Id: Guid.NewGuid(), Name: name, TradeType: TradeType.Withdrawal,
        Amount: 100m, CashAccountId: null, CategoryId: null,
        Frequency: RecurrenceFrequency.Monthly, Interval: 1,
        StartDate: nextDue, EndDate: null,
        GenerationMode: AutoGenerationMode.PendingConfirm,
        NextDueAt: nextDue);

    private sealed class FakeTradeRepo : ITradeRepository
    {
        public List<Trade> Store { get; } = new();
        public Task<IReadOnlyList<Trade>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<Trade>>(Store.ToList());
        public Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string loanLabel) =>
            Task.FromResult<IReadOnlyList<Trade>>(Store.Where(t => t.LoanLabel == loanLabel).ToList());
        public Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid cashAccountId) =>
            Task.FromResult<IReadOnlyList<Trade>>(Store.Where(t => t.CashAccountId == cashAccountId).ToList());
        public Task AddAsync(Trade t) { Store.Add(t); return Task.CompletedTask; }
        public Task UpdateAsync(Trade t) => Task.CompletedTask;
        public Task RemoveAsync(Guid id) => Task.CompletedTask;
        public Task RemoveChildrenAsync(Guid parentId) => Task.CompletedTask;
        public Task RemoveByAccountIdAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
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

    private sealed class FakeRecurringRepo : IRecurringTransactionRepository
    {
        public List<RecurringTransaction> Store { get; } = new();
        public Task<IReadOnlyList<RecurringTransaction>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RecurringTransaction>>(Store.ToList());
        public Task<IReadOnlyList<RecurringTransaction>> GetActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RecurringTransaction>>(Store.Where(r => r.IsEnabled).ToList());
        public Task<RecurringTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Store.FirstOrDefault(r => r.Id == id));
        public Task AddAsync(RecurringTransaction r, CancellationToken ct = default) { Store.Add(r); return Task.CompletedTask; }
        public Task UpdateAsync(RecurringTransaction r, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }
}
