using Assetra.Core.Interfaces;

namespace Assetra.Application.Assistant;

/// <summary>
/// AI Phase 3.5 — grounded tool registry backed by the same query services
/// the rule-based assistant uses. Tools return short snapshot strings the
/// LLM can splice into its context to answer with real numbers.
/// </summary>
public sealed class GroundedAssistantToolRegistry : IAssistantToolRegistry
{
    private readonly IBalanceQueryService _balances;
    private readonly IBudgetRepository? _budgets;
    private readonly TimeProvider _time;

    public GroundedAssistantToolRegistry(
        IBalanceQueryService balances,
        IBudgetRepository? budgets = null,
        TimeProvider? time = null)
    {
        _balances = balances ?? throw new ArgumentNullException(nameof(balances));
        _budgets = budgets;
        _time = time ?? TimeProvider.System;

        Tools = BuildTools().ToList();
    }

    public IReadOnlyList<AssistantTool> Tools { get; }

    public AssistantTool? Find(string name) =>
        Tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<AssistantTool> BuildTools()
    {
        yield return new AssistantTool(
            Name: "get_net_worth",
            Description: "Returns the user's current net worth: total cash minus total liabilities.",
            InvokeAsync: async ct =>
            {
                var cash = await _balances.GetAllCashBalancesAsync().ConfigureAwait(false);
                var liab = await _balances.GetAllLiabilitySnapshotsAsync().ConfigureAwait(false);
                var totalCash = cash.Values.Sum(m => m.Amount);
                var totalLiab = liab.Values.Sum(s => s.Balance.Amount);
                return $"net_worth={totalCash - totalLiab:N0}, cash={totalCash:N0}, liabilities={totalLiab:N0} (currencies not converted)";
            });

        yield return new AssistantTool(
            Name: "list_cash_balances",
            Description: "Lists current cash balances per account (top 8, descending).",
            InvokeAsync: async ct =>
            {
                var cash = await _balances.GetAllCashBalancesAsync().ConfigureAwait(false);
                var lines = cash
                    .OrderByDescending(kv => kv.Value.Amount)
                    .Take(8)
                    .Select(kv => $"{kv.Key.ToString()[..8]}: {kv.Value.Amount:N0} {kv.Value.Currency}");
                return string.Join("; ", lines);
            });

        yield return new AssistantTool(
            Name: "list_liabilities",
            Description: "Lists current outstanding liability balances grouped by loan label.",
            InvokeAsync: async ct =>
            {
                var liab = await _balances.GetAllLiabilitySnapshotsAsync().ConfigureAwait(false);
                if (liab.Count == 0)
                    return "No outstanding liabilities";
                var lines = liab
                    .OrderByDescending(kv => kv.Value.Balance.Amount)
                    .Select(kv => $"{kv.Key}: {kv.Value.Balance.Amount:N0} / {kv.Value.OriginalAmount.Amount:N0}");
                return string.Join("; ", lines);
            });

        if (_budgets is not null)
        {
            yield return new AssistantTool(
                Name: "get_current_month_budgets",
                Description: "Lists this month's category budgets and amounts (no actuals — combine with list_cash_balances + trade journal queries for spending vs. budget).",
                InvokeAsync: async ct =>
                {
                    var today = _time.GetUtcNow().LocalDateTime;
                    var budgets = await _budgets.GetByPeriodAsync(today.Year, today.Month, ct).ConfigureAwait(false);
                    if (budgets.Count == 0)
                        return "No budgets configured for this month";
                    var lines = budgets.Select(b =>
                        $"{(b.CategoryId?.ToString()[..8] ?? "all")}: {b.Amount:N0} {b.Currency}");
                    return string.Join("; ", lines);
                });
        }
    }
}
