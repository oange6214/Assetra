using Assetra.Application.Reports.Statements;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Core.Models.Reports;
using Xunit;

namespace Assetra.Tests.Application.Reports;

public class CashFlowStatementServiceTests
{
    [Fact]
    public async Task GenerateAsync_OpeningPlusNetEqualsClosing_AndSegmentsSum()
    {
        var trades = new FakeTradeRepo();
        // Pre-period (March): Income 10000 → opening cash 10000
        trades.Store.Add(MakeIncome(new DateTime(2026, 3, 1), 10000m));
        // April:
        // Operating: Income 5000 + Withdrawal 2000 → +3000
        trades.Store.Add(MakeIncome(new DateTime(2026, 4, 1), 5000m));
        trades.Store.Add(MakeWithdrawal(new DateTime(2026, 4, 5), 2000m));
        // Investing: Buy at 100*10 = 1000 → -1000
        trades.Store.Add(MakeBuy(new DateTime(2026, 4, 10), 100m, 10));
        // Financing: LoanBorrow 5000 → +5000
        trades.Store.Add(MakeLoanBorrow(new DateTime(2026, 4, 15), 5000m));

        var svc = new CashFlowStatementService(trades);
        var stmt = await svc.GenerateAsync(ReportPeriod.Month(2026, 4));

        Assert.Equal(10000m, stmt.OpeningCash);
        Assert.Equal(3000m, stmt.Operating.Total);
        Assert.Equal(-1000m, stmt.Investing.Total);
        Assert.Equal(5000m, stmt.Financing.Total);
        Assert.Equal(7000m, stmt.NetChange);
        Assert.Equal(17000m, stmt.ClosingCash);
        Assert.Equal(stmt.OpeningCash + stmt.NetChange, stmt.ClosingCash);
    }

    [Fact]
    public async Task GenerateAsync_EmptyJournal_ReturnsAllZero()
    {
        var svc = new CashFlowStatementService(new FakeTradeRepo());
        var stmt = await svc.GenerateAsync(ReportPeriod.Month(2026, 4));
        Assert.Equal(0m, stmt.OpeningCash);
        Assert.Equal(0m, stmt.NetChange);
        Assert.Equal(0m, stmt.ClosingCash);
    }

    private static Trade MakeIncome(DateTime when, decimal amt) =>
        new(Guid.NewGuid(), "", "", "i", TradeType.Income, when, 0m, 1, null, null, CashAmount: amt);
    private static Trade MakeWithdrawal(DateTime when, decimal amt) =>
        new(Guid.NewGuid(), "", "", "w", TradeType.Withdrawal, when, 0m, 1, null, null, CashAmount: amt);
    private static Trade MakeBuy(DateTime when, decimal price, int qty) =>
        new(Guid.NewGuid(), "X", "TW", "stock", TradeType.Buy, when, price, qty, null, null);
    private static Trade MakeLoanBorrow(DateTime when, decimal amt) =>
        new(Guid.NewGuid(), "", "", "loan", TradeType.LoanBorrow, when, 0m, 1, null, null,
            CashAmount: amt, LoanLabel: "TestLoan");

    private sealed class FakeTradeRepo : ITradeRepository
    {
        public List<Trade> Store { get; } = new();
        public Task<IReadOnlyList<Trade>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Trade>>(Store.ToList());
        public Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string l, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task<Trade?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Trade?>(null);
        public Task AddAsync(Trade t, CancellationToken ct = default) { Store.Add(t); return Task.CompletedTask; }
        public Task UpdateAsync(Trade t, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveChildrenAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByAccountIdAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByLiabilityAsync(Guid? id, string? l, CancellationToken ct = default) => Task.CompletedTask;
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
}
