namespace Assetra.Core.Models;

/// <summary>單一分類於指定期間的支出彙總與預算比較。</summary>
public sealed record CategorySpendSummary(
    Guid? CategoryId,
    string CategoryName,
    decimal Spent,
    decimal? BudgetAmount)
{
    public decimal? Remaining =>
        BudgetAmount.HasValue ? BudgetAmount.Value - Spent : null;

    public double? UsageRatio =>
        BudgetAmount is { } amt && amt > 0 ? (double)(Spent / amt) : null;

    public bool IsOverBudget =>
        BudgetAmount is { } amt && Spent > amt;
}

/// <summary>月度收支彙總。</summary>
public sealed record MonthlyBudgetSummary(
    int Year,
    int Month,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal? TotalBudget,
    IReadOnlyList<CategorySpendSummary> Categories)
{
    public decimal NetCashFlow => TotalIncome - TotalExpense;
}
