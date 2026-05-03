using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using CoreBudget = Assetra.Core.Models.Budget;

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

        var monthStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEnd = monthStart.AddMonths(1).AddTicks(-1);
        var monthTrades = (await _tradeRepository.GetByPeriodAsync(monthStart, monthEnd, ct).ConfigureAwait(false)).ToList();

        var totalIncome = monthTrades
            .Where(IsIncomeTrade)
            .Sum(t => t.CashAmount ?? 0m);

        var totalExpense = monthTrades
            .Where(IsExpenseTrade)
            .Sum(GetExpenseAmount);

        var monthlyBudgets = await _budgetRepository.GetByPeriodAsync(year, month, ct).ConfigureAwait(false);
        var yearlyBudgets = await _budgetRepository.GetByPeriodAsync(year, null, ct).ConfigureAwait(false);
        var budgets = BuildEffectiveMonthlyBudgets(monthlyBudgets, yearlyBudgets);
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

    private static IReadOnlyList<CoreBudget> BuildEffectiveMonthlyBudgets(
        IReadOnlyList<CoreBudget> monthlyBudgets,
        IReadOnlyList<CoreBudget> yearlyBudgets)
    {
        var monthlyKeys = monthlyBudgets
            .Select(b => b.CategoryId)
            .ToHashSet();

        var yearlyFallbacks = yearlyBudgets
            .Where(b => b.Mode == BudgetMode.Yearly && !monthlyKeys.Contains(b.CategoryId))
            .Select(b => b with
            {
                Amount = b.Amount / 12m,
            });

        return [.. monthlyBudgets, .. yearlyFallbacks];
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
