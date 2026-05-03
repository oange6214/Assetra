using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Interfaces.Reports;
using Assetra.Core.Models;
using Assetra.Core.Models.Reports;

namespace Assetra.Application.Reports.Statements;

/// <summary>
/// 損益表服務：依 ReportPeriod 取期間 trade，把 Income / Expense 兩類依分類聚合。
/// 同步計算等長度上一期（Prior）做 MoM/YoY 對照。
/// v0.23：加入租金收入（RentalIncome 區段）與保費支出（InsurancePremium 區段）。
/// </summary>
public sealed class IncomeStatementService : IIncomeStatementService
{
    private readonly ITradeRepository _trades;
    private readonly ICategoryRepository? _categories;
    private readonly IRentalIncomeRecordRepository? _rentalRecords;
    private readonly IInsurancePremiumRecordRepository? _premiumRecords;

    public IncomeStatementService(
        ITradeRepository trades,
        ICategoryRepository? categories = null,
        IRentalIncomeRecordRepository? rentalRecords = null,
        IInsurancePremiumRecordRepository? premiumRecords = null)
    {
        ArgumentNullException.ThrowIfNull(trades);
        _trades = trades;
        _categories = categories;
        _rentalRecords = rentalRecords;
        _premiumRecords = premiumRecords;
    }

    public async Task<IncomeStatement> GenerateAsync(ReportPeriod period, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(period);
        var prior = period.Prior();
        var from = prior.Start.ToDateTime(TimeOnly.MinValue);
        var to = period.End.ToDateTime(TimeOnly.MaxValue);
        var trades = await _trades.GetByPeriodAsync(from, to, ct).ConfigureAwait(false);
        var categories = _categories is null
            ? new List<ExpenseCategory>()
            : (await _categories.GetAllAsync(ct).ConfigureAwait(false)).ToList();

        var rentalRows = await BuildRentalRowsAsync(period, ct).ConfigureAwait(false);
        var premiumRows = await BuildPremiumRowsAsync(period, ct).ConfigureAwait(false);
        var priorRentalRows = await BuildRentalRowsAsync(prior, ct).ConfigureAwait(false);
        var priorPremiumRows = await BuildPremiumRowsAsync(prior, ct).ConfigureAwait(false);

        var priorStatement = Build(prior, trades, categories, priorRentalRows, priorPremiumRows);
        return Build(period, trades, categories, rentalRows, premiumRows, priorStatement);
    }

    private IncomeStatement Build(
        ReportPeriod period,
        IReadOnlyList<Trade> allTrades,
        IReadOnlyList<ExpenseCategory> categories,
        IReadOnlyList<StatementRow> rentalRows,
        IReadOnlyList<StatementRow> premiumRows,
        IncomeStatement? prior = null)
    {
        var inPeriod = allTrades.Where(t => period.Contains(t.TradeDate)).ToList();

        var incomeSection = BuildSection(
            title: "Income",
            trades: inPeriod.Where(t => t.Type == TradeType.Income),
            categories: categories,
            categoryKind: CategoryKind.Income);

        // Merge rental income rows into income section
        var incomeRows = incomeSection.Rows.ToList();
        incomeRows.AddRange(rentalRows);
        var income = new StatementSection("Income", incomeRows, incomeRows.Sum(r => r.Amount));

        var expenseSection = BuildSection(
            title: "Expense",
            trades: inPeriod.Where(t => IsExpense(t)),
            categories: categories,
            categoryKind: CategoryKind.Expense);

        // Merge premium rows into expense section
        var expenseRows = expenseSection.Rows.ToList();
        expenseRows.AddRange(premiumRows);
        var expense = new StatementSection("Expense", expenseRows, expenseRows.Sum(r => r.Amount));

        var net = income.Total - expense.Total;

        return new IncomeStatement(period, income, expense, net, prior);
    }

    private async Task<IReadOnlyList<StatementRow>> BuildRentalRowsAsync(
        ReportPeriod period, CancellationToken ct)
    {
        if (_rentalRecords is null) return Array.Empty<StatementRow>();
        var records = await _rentalRecords
            .GetByPeriodAsync(period.Start, period.End, ct).ConfigureAwait(false);
        if (records.Count == 0) return Array.Empty<StatementRow>();
        var total = records.Sum(r => r.NetIncome);
        return new[] { new StatementRow("Rental Income", Math.Max(total, 0m), "Real Estate") };
    }

    private async Task<IReadOnlyList<StatementRow>> BuildPremiumRowsAsync(
        ReportPeriod period, CancellationToken ct)
    {
        if (_premiumRecords is null) return Array.Empty<StatementRow>();
        var records = await _premiumRecords
            .GetByPeriodAsync(period.Start, period.End, ct).ConfigureAwait(false);
        if (records.Count == 0) return Array.Empty<StatementRow>();
        var total = records.Sum(r => r.Amount);
        return new[] { new StatementRow("Insurance Premiums", total, "Insurance") };
    }

    private static bool IsExpense(Trade t)
    {
        // Treat regular CreditCardCharge as expense (cash-equivalent outflow);
        // LoanRepay InterestPaid also expense but principal is financing — handled in CashFlow.
        return t.Type == TradeType.CreditCardCharge ||
               t.Type == TradeType.Withdrawal;
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
