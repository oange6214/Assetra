using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Assistant;

/// <summary>
/// AI Phase 2 — rule-based insight generator. Pulls from <see cref="IBudgetRepository"/>,
/// <see cref="IRecurringTransactionRepository"/>, and the <see cref="ITradeRepository"/>
/// trade journal to produce actionable alerts. No LLM, no schedule — caller
/// (AssistantViewModel.LoadInsights) drives execution.
///
/// <para>
/// Three rule families:
/// <list type="bullet">
///   <item><b>Budget overspending</b> — for each Monthly budget on the current month,
///         compare against current-month outgoing trades in that category. &gt;= 100%
///         is Critical, &gt;= 80% is Warning.</item>
///   <item><b>Recurring upcoming</b> — items with NextDueAt within the next 7 days
///         (Info level). Disabled subscriptions excluded.</item>
///   <item><b>Month delta</b> — net cash-flow this month vs last month (income − outgoings).
///         Negative shift &gt;= 30% triggers Warning.</item>
/// </list>
/// </para>
/// </summary>
public sealed class RuleBasedAssistantInsightService : IAssistantInsightService
{
    private readonly IBudgetRepository? _budgets;
    private readonly IRecurringTransactionRepository? _recurring;
    private readonly ITradeRepository? _trades;
    private readonly TimeProvider _time;

    public RuleBasedAssistantInsightService(
        IBudgetRepository? budgets = null,
        IRecurringTransactionRepository? recurring = null,
        ITradeRepository? trades = null,
        TimeProvider? time = null)
    {
        _budgets = budgets;
        _recurring = recurring;
        _trades = trades;
        _time = time ?? TimeProvider.System;
    }

    public async Task<IReadOnlyList<AssistantInsight>> GetCurrentInsightsAsync(CancellationToken ct = default)
    {
        var insights = new List<AssistantInsight>();
        var today = DateOnly.FromDateTime(_time.GetUtcNow().LocalDateTime);

        await AddBudgetInsightsAsync(insights, today, ct).ConfigureAwait(false);
        await AddRecurringInsightsAsync(insights, today, ct).ConfigureAwait(false);
        await AddMonthDeltaInsightAsync(insights, today, ct).ConfigureAwait(false);

        // Sort by severity (Critical first), then alphabetic title for stable order.
        return insights
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.Title, StringComparer.Ordinal)
            .ToList();
    }

    private async Task AddBudgetInsightsAsync(List<AssistantInsight> insights, DateOnly today, CancellationToken ct)
    {
        if (_budgets is null || _trades is null)
            return;
        var monthly = await _budgets.GetByPeriodAsync(today.Year, today.Month, ct).ConfigureAwait(false);
        if (monthly.Count == 0)
            return;

        var trades = await _trades.GetAllAsync(ct).ConfigureAwait(false);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1);

        // Outgoings = Withdrawal + Buy + LoanRepay + CreditCardCharge ... but for
        // budget purposes only Withdrawal + Income (negative) typically applies.
        // Use the simple convention: Withdrawal trades count against the category budget.
        var spendByCategory = trades
            .Where(t => t.TradeDate >= monthStart && t.TradeDate < monthEnd
                     && t.Type == TradeType.Withdrawal
                     && t.CategoryId.HasValue)
            .GroupBy(t => t.CategoryId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.CashAmount ?? 0m));

        foreach (var b in monthly.Where(b => b.CategoryId.HasValue && b.Mode == BudgetMode.Monthly && b.Amount > 0))
        {
            var spent = spendByCategory.TryGetValue(b.CategoryId!.Value, out var s) ? s : 0m;
            var ratio = spent / b.Amount;
            if (ratio < 0.8m)
                continue;
            var severity = ratio >= 1m ? AssistantInsightSeverity.Critical : AssistantInsightSeverity.Warning;
            insights.Add(new AssistantInsight(
                Severity: severity,
                Title: $"預算 {(ratio >= 1m ? "超支" : "接近上限")}（{ratio:P0}）",
                Description: $"分類 {b.CategoryId} 本月預算 {b.Amount:N0} {b.Currency}，已支出 {spent:N0}（{ratio:P0}）。",
                Source: "Budget"));
        }
    }

    private async Task AddRecurringInsightsAsync(List<AssistantInsight> insights, DateOnly today, CancellationToken ct)
    {
        if (_recurring is null)
            return;
        var active = await _recurring.GetActiveAsync(ct).ConfigureAwait(false);
        if (active.Count == 0)
            return;

        var horizon = today.AddDays(7);
        foreach (var r in active.Where(r => r.NextDueAt.HasValue))
        {
            var due = DateOnly.FromDateTime(r.NextDueAt!.Value);
            if (due > horizon || due < today)
                continue;
            insights.Add(new AssistantInsight(
                Severity: AssistantInsightSeverity.Info,
                Title: $"訂閱即將到期：{r.Name}",
                Description: $"{r.Name} 預計於 {due:yyyy-MM-dd}（{(due.DayNumber - today.DayNumber)} 天後）扣款 {r.Amount:N0}。",
                Source: "Recurring"));
        }
    }

    private async Task AddMonthDeltaInsightAsync(List<AssistantInsight> insights, DateOnly today, CancellationToken ct)
    {
        if (_trades is null)
            return;
        var trades = await _trades.GetAllAsync(ct).ConfigureAwait(false);
        var thisMonthStart = new DateTime(today.Year, today.Month, 1);
        var lastMonthStart = thisMonthStart.AddMonths(-1);
        var thisNet = NetCashFlow(trades, thisMonthStart, thisMonthStart.AddMonths(1));
        var lastNet = NetCashFlow(trades, lastMonthStart, thisMonthStart);

        // Only generate insight if last month had meaningful activity.
        if (Math.Abs(lastNet) < 10_000m)
            return;
        var delta = thisNet - lastNet;
        var ratio = delta / Math.Abs(lastNet);
        if (ratio >= -0.30m)
            return;  // not a >= 30% drop
        insights.Add(new AssistantInsight(
            Severity: AssistantInsightSeverity.Warning,
            Title: $"本月淨現金流下滑 {Math.Abs(ratio):P0}",
            Description: $"上月淨現金流 {lastNet:N0}，本月至今 {thisNet:N0}（差額 {delta:N0}）。",
            Source: "MonthDelta"));
    }

    private static decimal NetCashFlow(IReadOnlyList<Trade> trades, DateTime from, DateTime to)
    {
        decimal income = 0, outgoings = 0;
        foreach (var t in trades.Where(t => t.TradeDate >= from && t.TradeDate < to))
        {
            switch (t.Type)
            {
                case TradeType.Income:
                case TradeType.CashDividend:
                case TradeType.Deposit:
                    income += t.CashAmount ?? 0m;
                    break;
                case TradeType.Withdrawal:
                case TradeType.LoanRepay:
                case TradeType.CreditCardPayment:
                    outgoings += t.CashAmount ?? 0m;
                    break;
            }
        }
        return income - outgoings;
    }
}
