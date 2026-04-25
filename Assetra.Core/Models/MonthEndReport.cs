namespace Assetra.Core.Models;

/// <summary>
/// 月末結算報告：當月 vs 上月對照、預算超支警示、近期到期訂閱清單。
/// </summary>
public sealed record MonthEndReport(
    int Year,
    int Month,
    MonthlyBudgetSummary Current,
    MonthlyBudgetSummary? Previous,
    IReadOnlyList<CategorySpendSummary> OverBudgetCategories,
    IReadOnlyList<UpcomingRecurringItem> Upcoming)
{
    public decimal IncomeDelta   => Current.TotalIncome  - (Previous?.TotalIncome  ?? 0m);
    public decimal ExpenseDelta  => Current.TotalExpense - (Previous?.TotalExpense ?? 0m);
    public decimal NetDelta      => Current.NetCashFlow  - (Previous?.NetCashFlow  ?? 0m);
    public bool    HasAlerts     => OverBudgetCategories.Count > 0;
    public decimal SavingsRate   => Current.TotalIncome > 0
        ? Current.NetCashFlow / Current.TotalIncome
        : 0m;
}

public sealed record UpcomingRecurringItem(
    Guid RecurringId,
    string Name,
    DateTime DueDate,
    decimal Amount,
    TradeType TradeType);
