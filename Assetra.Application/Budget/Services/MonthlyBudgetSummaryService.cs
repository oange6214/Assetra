using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Budget.Services;

/// <summary>
/// 將某月的現金流（Income / Withdrawal / CreditCardCharge / LoanRepay 利息）
/// 與該月預算彙總成 <see cref="MonthlyBudgetSummary"/>。
/// </summary>
public sealed class MonthlyBudgetSummaryService
{
    private readonly ITradeRepository _tradeRepository;
    private readonly IBudgetRepository _budgetRepository;
    private readonly ICategoryRepository _categoryRepository;

    public MonthlyBudgetSummaryService(
        ITradeRepository tradeRepository,
        IBudgetRepository budgetRepository,
        ICategoryRepository categoryRepository)
    {
        ArgumentNullException.ThrowIfNull(tradeRepository);
        ArgumentNullException.ThrowIfNull(budgetRepository);
        ArgumentNullException.ThrowIfNull(categoryRepository);
        _tradeRepository = tradeRepository;
        _budgetRepository = budgetRepository;
        _categoryRepository = categoryRepository;
    }

    public async Task<MonthlyBudgetSummary> BuildAsync(int year, int month, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var trades = await _tradeRepository.GetAllAsync().ConfigureAwait(false);
        var monthTrades = trades.Where(t => t.TradeDate.Year == year && t.TradeDate.Month == month).ToList();

        var totalIncome = monthTrades
            .Where(IsIncomeTrade)
            .Sum(t => t.CashAmount ?? 0m);

        var totalExpense = monthTrades
            .Where(IsExpenseTrade)
            .Sum(GetExpenseAmount);

        var budgets = await _budgetRepository.GetByPeriodAsync(year, month, ct).ConfigureAwait(false);
        var totalBudget = budgets.FirstOrDefault(b => b.CategoryId is null)?.Amount;
        var budgetByCategory = budgets
            .Where(b => b.CategoryId.HasValue)
            .ToDictionary(b => b.CategoryId!.Value, b => b.Amount);

        var categories = await _categoryRepository.GetAllAsync(ct).ConfigureAwait(false);
        var categoryNameById = categories.ToDictionary(c => c.Id, c => c.Name);

        var categorySpend = monthTrades
            .Where(IsExpenseTrade)
            .GroupBy(t => t.CategoryId)
            .Select(g =>
            {
                var spent = g.Sum(GetExpenseAmount);
                var name = g.Key.HasValue && categoryNameById.TryGetValue(g.Key.Value, out var n)
                    ? n
                    : string.Empty;
                decimal? budget = g.Key.HasValue && budgetByCategory.TryGetValue(g.Key.Value, out var b)
                    ? b
                    : null;
                return new CategorySpendSummary(g.Key, name, spent, budget);
            })
            .OrderByDescending(c => c.Spent)
            .ToList();

        return new MonthlyBudgetSummary(year, month, totalIncome, totalExpense, totalBudget, categorySpend);
    }

    private static bool IsIncomeTrade(Trade t) =>
        t.Type is TradeType.Income or TradeType.Deposit;

    private static bool IsExpenseTrade(Trade t) =>
        t.Type is TradeType.Withdrawal
            or TradeType.CreditCardCharge
            or TradeType.LoanRepay;

    private static decimal GetExpenseAmount(Trade t) => t.Type switch
    {
        TradeType.Withdrawal => t.CashAmount ?? 0m,
        TradeType.CreditCardCharge => t.CashAmount ?? 0m,
        TradeType.LoanRepay => t.InterestPaid ?? 0m,
        _ => 0m,
    };
}
