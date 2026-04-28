using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Reports;
using Assetra.Core.Models;
using Assetra.Core.Models.Reports;

namespace Assetra.Application.Reports.Statements;

/// <summary>
/// 損益表服務：依 ReportPeriod 取期間 trade，把 Income / Expense 兩類依分類聚合。
/// 同步計算等長度上一期（Prior）做 MoM/YoY 對照。
/// </summary>
public sealed class IncomeStatementService : IIncomeStatementService
{
    private readonly ITradeRepository _trades;
    private readonly ICategoryRepository? _categories;

    public IncomeStatementService(ITradeRepository trades, ICategoryRepository? categories = null)
    {
        ArgumentNullException.ThrowIfNull(trades);
        _trades = trades;
        _categories = categories;
    }

    public async Task<IncomeStatement> GenerateAsync(ReportPeriod period, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(period);
        var trades = await _trades.GetAllAsync(ct).ConfigureAwait(false);
        var categories = _categories is null
            ? new List<ExpenseCategory>()
            : (await _categories.GetAllAsync(ct).ConfigureAwait(false)).ToList();

        return Build(period, trades, categories, includePrior: true);
    }

    private IncomeStatement Build(
        ReportPeriod period,
        IReadOnlyList<Trade> allTrades,
        IReadOnlyList<ExpenseCategory> categories,
        bool includePrior)
    {
        var inPeriod = allTrades.Where(t => period.Contains(t.TradeDate)).ToList();

        var income = BuildSection(
            title: "Income",
            trades: inPeriod.Where(t => t.Type == TradeType.Income),
            categories: categories,
            categoryKind: CategoryKind.Income);

        var expense = BuildSection(
            title: "Expense",
            trades: inPeriod.Where(t => IsExpense(t)),
            categories: categories,
            categoryKind: CategoryKind.Expense);

        var net = income.Total - expense.Total;

        IncomeStatement? prior = null;
        if (includePrior)
            prior = Build(period.Prior(), allTrades, categories, includePrior: false);

        return new IncomeStatement(period, income, expense, net, prior);
    }

    private static bool IsExpense(Trade t)
    {
        // Treat regular CreditCardCharge as expense (cash-equivalent outflow);
        // LoanRepay InterestPaid also expense but principal is financing — handled in CashFlow.
        return t.Type == TradeType.CreditCardCharge ||
               (t.Type == TradeType.Withdrawal && t.CategoryId is not null);
    }

    private static StatementSection BuildSection(
        string title,
        IEnumerable<Trade> trades,
        IReadOnlyList<ExpenseCategory> categories,
        CategoryKind categoryKind)
    {
        var lookup = categories
            .Where(c => c.Kind == categoryKind)
            .ToDictionary(c => c.Id, c => c.Name);

        var rows = trades
            .GroupBy(t => t.CategoryId)
            .Select(g =>
            {
                var label = g.Key is { } cid && lookup.TryGetValue(cid, out var n)
                    ? n
                    : "(Uncategorized)";
                var amt = g.Sum(t => t.CashAmount ?? 0m);
                return new StatementRow(label, Math.Abs(amt));
            })
            .OrderByDescending(r => r.Amount)
            .ToList();

        var total = rows.Sum(r => r.Amount);
        return new StatementSection(title, rows, total);
    }
}
