using Assetra.Application.Budget.Services;
using Assetra.Application.Reports.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Reports;
using Assetra.Core.Models;
using Assetra.Core.Models.Reports;
using Assetra.WPF.Features.Reports;
using Xunit;

namespace Assetra.Tests.WPF;

public sealed class ReportsViewModelTests
{
    [Fact]
    public async Task LoadAsync_NotifiesComputedRowsWhenReportChanges()
    {
        var cat = Guid.NewGuid();
        var reportService = CreateReportService(
            trades:
            [
                MakeWithdrawal(new DateTime(2026, 4, 5), 2500m, cat),
            ],
            budgets:
            [
                new Budget(Guid.NewGuid(), cat, BudgetMode.Monthly, 2026, 4, 1000m),
            ],
            categories:
            [
                new ExpenseCategory(cat, "Food", CategoryKind.Expense),
            ],
            recurring:
            [
                MakeRecurring("Rent", DateTime.Today.AddDays(2)),
            ]);
        var vm = new ReportsViewModel(reportService);
        vm.Year = 2026;
        vm.Month = 4;
        var notified = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null)
                notified.Add(e.PropertyName);
        };

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Contains(nameof(ReportsViewModel.OverBudgetRows), notified);
        Assert.Contains(nameof(ReportsViewModel.UpcomingRows), notified);
        Assert.Single(vm.OverBudgetRows);
        Assert.Single(vm.UpcomingRows);
    }

    [Fact]
    public async Task LoadAsync_WhenStatementFails_ClearsPriorStatementState()
    {
        var reportService = CreateReportService();
        var incomeService = new ToggleIncomeStatementService();
        var vm = new ReportsViewModel(reportService, incomeService: incomeService);
        vm.Year = 2026;
        vm.Month = 4;

        await vm.LoadCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Report);
        Assert.NotNull(vm.IncomeStatement);

        incomeService.ThrowOnGenerate = true;
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.Null(vm.Report);
        Assert.Null(vm.IncomeStatement);
        Assert.Null(vm.BalanceSheet);
        Assert.Null(vm.CashFlowStatement);
        // Performance / Risk fields removed from ReportsViewModel — see commit log.
        Assert.NotNull(vm.ErrorMessage);
    }

    private static MonthEndReportService CreateReportService(
        IReadOnlyList<Trade>? trades = null,
        IReadOnlyList<Budget>? budgets = null,
        IReadOnlyList<ExpenseCategory>? categories = null,
        IReadOnlyList<RecurringTransaction>? recurring = null)
    {
        var tradeRepo = new FakeTradeRepo(trades ?? []);
        var budgetRepo = new FakeBudgetRepo(budgets ?? []);
        var categoryRepo = new FakeCategoryRepo(categories ?? []);
        var recurringRepo = new FakeRecurringRepo(recurring ?? []);
        var summary = new MonthlyBudgetSummaryService(tradeRepo, budgetRepo, categoryRepo);
        return new MonthEndReportService(summary, recurringRepo);
    }

    private static Trade MakeWithdrawal(DateTime when, decimal amount, Guid categoryId) =>
        new(Guid.NewGuid(), "", "", "withdrawal", TradeType.Withdrawal, when, 0m, 1, null, null,
            CashAmount: amount, CategoryId: categoryId);

    private static RecurringTransaction MakeRecurring(string name, DateTime nextDue) => new(
        Id: Guid.NewGuid(), Name: name, TradeType: TradeType.Withdrawal,
        Amount: 100m, CashAccountId: null, CategoryId: null,
        Frequency: RecurrenceFrequency.Monthly, Interval: 1,
        StartDate: nextDue, EndDate: null,
        GenerationMode: AutoGenerationMode.PendingConfirm,
        NextDueAt: nextDue);

    private sealed class ToggleIncomeStatementService : IIncomeStatementService
    {
        public bool ThrowOnGenerate { get; set; }

        public Task<IncomeStatement> GenerateAsync(ReportPeriod period, CancellationToken ct = default)
        {
            if (ThrowOnGenerate)
                throw new InvalidOperationException("statement failed");

            var income = new StatementSection("Income", [], 0m);
            var expense = new StatementSection("Expense", [], 0m);
            return Task.FromResult(new IncomeStatement(period, income, expense, 0m));
        }
    }

    private sealed class FakeTradeRepo(IReadOnlyList<Trade> data) : ITradeRepository
    {
        public Task<IReadOnlyList<Trade>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult(data);

        public Task<IReadOnlyList<Trade>> GetByPeriodAsync(DateTime from, DateTime to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Trade>>(data.Where(t => t.TradeDate >= from && t.TradeDate <= to).ToList());

        public Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid cashAccountId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string loanLabel, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task<Trade?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Trade?>(null);
        public Task AddAsync(Trade trade, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Trade trade, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveChildrenAsync(Guid parentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByAccountIdAsync(Guid accountId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByLiabilityAsync(Guid? liabilityAssetId, string? loanLabel, CancellationToken ct = default) => Task.CompletedTask;
        public Task ApplyAtomicAsync(IReadOnlyList<TradeMutation> mutations, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeBudgetRepo(IReadOnlyList<Budget> data) : IBudgetRepository
    {
        public Task<IReadOnlyList<Budget>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(data);
        public Task<Budget?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(data.FirstOrDefault(b => b.Id == id));
        public Task AddAsync(Budget budget, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Budget budget, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Budget>> GetByPeriodAsync(int year, int? month, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Budget>>(data.Where(b => b.Year == year && b.Month == month).ToList());
    }

    private sealed class FakeCategoryRepo(IReadOnlyList<ExpenseCategory> data) : ICategoryRepository
    {
        public Task<IReadOnlyList<ExpenseCategory>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(data);
        public Task<ExpenseCategory?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(data.FirstOrDefault(c => c.Id == id));
        public Task AddAsync(ExpenseCategory category, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(ExpenseCategory category, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> AnyAsync(CancellationToken ct = default) => Task.FromResult(data.Count > 0);
    }

    private sealed class FakeRecurringRepo(IReadOnlyList<RecurringTransaction> data) : IRecurringTransactionRepository
    {
        public Task<IReadOnlyList<RecurringTransaction>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(data);
        public Task<IReadOnlyList<RecurringTransaction>> GetActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RecurringTransaction>>(data.Where(r => r.IsEnabled).ToList());
        public Task<RecurringTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(data.FirstOrDefault(r => r.Id == id));
        public Task AddAsync(RecurringTransaction recurring, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(RecurringTransaction recurring, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }
}
