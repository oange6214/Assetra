using Assetra.Application.Budget.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Reports.Services;

/// <summary>
/// 組裝月末結算報告：當月 + 上月 budget summary、預算超支清單、未來 14 天的訂閱到期項目。
/// </summary>
public sealed class MonthEndReportService
{
    private readonly MonthlyBudgetSummaryService _summaryService;
    private readonly IRecurringTransactionRepository _recurringRepository;

    public MonthEndReportService(
        MonthlyBudgetSummaryService summaryService,
        IRecurringTransactionRepository recurringRepository)
    {
        ArgumentNullException.ThrowIfNull(summaryService);
        ArgumentNullException.ThrowIfNull(recurringRepository);
        _summaryService = summaryService;
        _recurringRepository = recurringRepository;
    }

    public async Task<MonthEndReport> BuildAsync(int year, int month, CancellationToken ct = default)
    {
        var current = await _summaryService.BuildAsync(year, month, ct).ConfigureAwait(false);

        var (prevYear, prevMonth) = month == 1 ? (year - 1, 12) : (year, month - 1);
        MonthlyBudgetSummary? previous = null;
        try
        {
            previous = await _summaryService.BuildAsync(prevYear, prevMonth, ct).ConfigureAwait(false);
        }
        catch
        {
            // Previous month optional — failures should not break the report.
        }

        var overBudget = current.Categories
            .Where(c => c.IsOverBudget)
            .ToList();

        var upcoming = await BuildUpcomingAsync(DateTime.Today, 14, ct).ConfigureAwait(false);

        return new MonthEndReport(year, month, current, previous, overBudget, upcoming);
    }

    private async Task<IReadOnlyList<UpcomingRecurringItem>> BuildUpcomingAsync(
        DateTime fromDate, int daysAhead, CancellationToken ct)
    {
        var horizon = fromDate.AddDays(daysAhead);
        var active = await _recurringRepository.GetActiveAsync(ct).ConfigureAwait(false);
        return active
            .Where(r => r.NextDueAt is { } due && due >= fromDate && due <= horizon)
            .OrderBy(r => r.NextDueAt)
            .Select(r => new UpcomingRecurringItem(
                r.Id, r.Name, r.NextDueAt!.Value, r.Amount, r.TradeType))
            .ToList();
    }
}
