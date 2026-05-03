using Assetra.Application.Reports.Statements;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Reports;
using Xunit;

namespace Assetra.Tests.Application.Reports;

public class IncomeStatementServiceTests
{
    [Fact]
    public async Task GenerateAsync_GroupsIncomeAndExpenseByCategory_AndComputesNet()
    {
        var foodCat = Guid.NewGuid();
        var salaryCat = Guid.NewGuid();
        var trades = new FakeTradeRepo();
        var categories = new FakeCategoryRepo
        {
            Items =
            {
                new ExpenseCategory(foodCat, "Food", CategoryKind.Expense),
                new ExpenseCategory(salaryCat, "Salary", CategoryKind.Income),
            },
        };
        // April income 50k (Salary), expense 2000 (Food)
        trades.Store.Add(MakeIncome(new DateTime(2026, 4, 1), 50000m, salaryCat));
        trades.Store.Add(MakeWithdrawal(new DateTime(2026, 4, 5), 2000m, foodCat));
        // March (prior) income 40k, expense 1500
        trades.Store.Add(MakeIncome(new DateTime(2026, 3, 15), 40000m, salaryCat));
        trades.Store.Add(MakeWithdrawal(new DateTime(2026, 3, 20), 1500m, foodCat));

        var svc = new IncomeStatementService(trades, categories);
        var report = await svc.GenerateAsync(ReportPeriod.Month(2026, 4));

        Assert.Equal(50000m, report.Income.Total);
        Assert.Equal(2000m, report.Expense.Total);
        Assert.Equal(48000m, report.Net);
        Assert.NotNull(report.Prior);
        Assert.Equal(40000m, report.Prior!.Income.Total);
        Assert.Equal(1500m, report.Prior.Expense.Total);
    }

    [Fact]
    public async Task GenerateAsync_EmptyPeriod_ReturnsZeroTotals()
    {
        var svc = new IncomeStatementService(new FakeTradeRepo(), new FakeCategoryRepo());
        var report = await svc.GenerateAsync(ReportPeriod.Month(2026, 4));

        Assert.Equal(0m, report.Income.Total);
        Assert.Equal(0m, report.Expense.Total);
        Assert.Equal(0m, report.Net);
    }

    [Fact]
    public async Task GenerateAsync_UncategorizedRowsLabeledAsSuch()
    {
        var trades = new FakeTradeRepo();
        trades.Store.Add(MakeIncome(new DateTime(2026, 4, 1), 1000m, categoryId: null));
        var svc = new IncomeStatementService(trades, new FakeCategoryRepo());

        var report = await svc.GenerateAsync(ReportPeriod.Month(2026, 4));
        Assert.Single(report.Income.Rows);
        Assert.Equal("(Uncategorized)", report.Income.Rows[0].Label);
    }

    [Fact]
    public async Task GenerateAsync_UncategorizedWithdrawal_IsIncludedAsExpense()
    {
        var trades = new FakeTradeRepo();
        trades.Store.Add(new Trade(
            Guid.NewGuid(), "", "", "expense", TradeType.Withdrawal,
            new DateTime(2026, 4, 5), 0m, 1, null, null,
            CashAmount: 2500m));
        var svc = new IncomeStatementService(trades, new FakeCategoryRepo());

        var report = await svc.GenerateAsync(ReportPeriod.Month(2026, 4));

        Assert.Equal(2500m, report.Expense.Total);
        var row = Assert.Single(report.Expense.Rows);
        Assert.Equal("(Uncategorized)", row.Label);
        Assert.Equal(2500m, row.Amount);
    }

    [Fact]
    public async Task GenerateAsync_PriorIncludesRentalIncomeAndInsurancePremiums()
    {
        var rentalRecords = new FakeRentalIncomeRecordRepo
        {
            Items =
            {
                new RentalIncomeRecord(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 3, 10), 30000m, 5000m, "TWD", null),
                new RentalIncomeRecord(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 4, 1), 10000m, 1000m, "TWD", null),
            },
        };
        var premiumRecords = new FakeInsurancePremiumRecordRepo
        {
            Items =
            {
                new InsurancePremiumRecord(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 3, 10), 6000m, "TWD", null),
                new InsurancePremiumRecord(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 4, 10), 2000m, "TWD", null),
            },
        };
        var svc = new IncomeStatementService(
            new FakeTradeRepo(),
            new FakeCategoryRepo(),
            rentalRecords,
            premiumRecords);

        var report = await svc.GenerateAsync(ReportPeriod.Month(2026, 4));

        Assert.NotNull(report.Prior);
        Assert.Equal(25000m, report.Prior!.Income.Total);
        Assert.Equal(6000m, report.Prior.Expense.Total);
        Assert.Equal(19000m, report.Prior.Net);
    }

    private static Trade MakeIncome(DateTime when, decimal amount, Guid? categoryId) => new(
        Guid.NewGuid(), string.Empty, string.Empty, "income",
        TradeType.Income, when, 0m, 1, null, null,
        CashAmount: amount, CategoryId: categoryId);

    private static Trade MakeWithdrawal(DateTime when, decimal amount, Guid categoryId) => new(
        Guid.NewGuid(), string.Empty, string.Empty, "expense",
        TradeType.Withdrawal, when, 0m, 1, null, null,
        CashAmount: amount, CategoryId: categoryId);

    private sealed class FakeTradeRepo : ITradeRepository
    {
        public List<Trade> Store { get; } = new();
        public Task<IReadOnlyList<Trade>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Trade>>(Store.ToList());
        public Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string loanLabel, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Trade>>(Store.Where(t => t.LoanLabel == loanLabel).ToList());
        public Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Trade>>(Store.Where(t => t.CashAccountId == id).ToList());
        public Task<Trade?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<Trade?>(Store.FirstOrDefault(t => t.Id == id));
        public Task AddAsync(Trade t, CancellationToken ct = default) { Store.Add(t); return Task.CompletedTask; }
        public Task UpdateAsync(Trade t, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveChildrenAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByAccountIdAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByLiabilityAsync(Guid? id, string? label, CancellationToken ct = default) => Task.CompletedTask;
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

    private sealed class FakeRentalIncomeRecordRepo : IRentalIncomeRecordRepository
    {
        public List<RentalIncomeRecord> Items { get; } = [];

        public Task<IReadOnlyList<RentalIncomeRecord>> GetByPropertyAsync(Guid realEstateId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RentalIncomeRecord>>(Items.Where(r => r.RealEstateId == realEstateId).ToList());

        public Task<IReadOnlyList<RentalIncomeRecord>> GetByPeriodAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RentalIncomeRecord>>(Items.Where(r => r.Month >= from && r.Month <= to).ToList());

        public Task AddAsync(RentalIncomeRecord record, CancellationToken ct = default) { Items.Add(record); return Task.CompletedTask; }
        public Task UpdateAsync(RentalIncomeRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeInsurancePremiumRecordRepo : IInsurancePremiumRecordRepository
    {
        public List<InsurancePremiumRecord> Items { get; } = [];

        public Task<IReadOnlyList<InsurancePremiumRecord>> GetByPolicyAsync(Guid policyId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<InsurancePremiumRecord>>(Items.Where(r => r.PolicyId == policyId).ToList());

        public Task<IReadOnlyList<InsurancePremiumRecord>> GetByPeriodAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<InsurancePremiumRecord>>(Items.Where(r => r.PaidDate >= from && r.PaidDate <= to).ToList());

        public Task AddAsync(InsurancePremiumRecord record, CancellationToken ct = default) { Items.Add(record); return Task.CompletedTask; }
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }
}
