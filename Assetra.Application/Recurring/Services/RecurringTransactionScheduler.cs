using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Application.Recurring.Services;

/// <summary>
/// 推進週期性交易排程：
/// <list type="bullet">
///   <item>讀取所有 enabled 的 <see cref="RecurringTransaction"/></item>
///   <item>依其 NextDueAt 反覆向前推進到當前時間，每次到期視 GenerationMode 處理：
///     <list type="bullet">
///       <item><see cref="AutoGenerationMode.AutoApply"/> → 直接寫入 <see cref="Trade"/></item>
///       <item><see cref="AutoGenerationMode.PendingConfirm"/> → 建立 <see cref="PendingRecurringEntry"/></item>
///     </list>
///   </item>
///   <item>最後將 LastGeneratedAt / NextDueAt 回寫</item>
/// </list>
/// </summary>
public sealed class RecurringTransactionScheduler
{
    private readonly IRecurringTransactionRepository _recurringRepo;
    private readonly IPendingRecurringEntryRepository _pendingRepo;
    private readonly ITransactionService _transactionService;

    public RecurringTransactionScheduler(
        IRecurringTransactionRepository recurringRepo,
        IPendingRecurringEntryRepository pendingRepo,
        ITransactionService transactionService)
    {
        _recurringRepo = recurringRepo;
        _pendingRepo = pendingRepo;
        _transactionService = transactionService;
    }

    /// <summary>處理所有到期的週期性交易。回傳本次推進共產生（auto + pending）幾筆。</summary>
    public async Task<RecurringRunResult> RunAsync(DateTime now, CancellationToken ct = default)
    {
        var active = await _recurringRepo.GetActiveAsync(ct).ConfigureAwait(false);
        var autoApplied = 0;
        var pendingCreated = 0;

        foreach (var recurring in active)
        {
            ct.ThrowIfCancellationRequested();
            var next = recurring.NextDueAt ?? recurring.StartDate;
            var lastGenerated = recurring.LastGeneratedAt;

            while (next <= now && (recurring.EndDate is null || next <= recurring.EndDate))
            {
                if (recurring.GenerationMode == AutoGenerationMode.AutoApply)
                {
                    await _transactionService.RecordAsync(BuildTrade(recurring, next)).ConfigureAwait(false);
                    autoApplied++;
                }
                else
                {
                    await _pendingRepo.AddAsync(BuildPending(recurring, next), ct).ConfigureAwait(false);
                    pendingCreated++;
                }

                lastGenerated = next;
                next = Advance(next, recurring.Frequency, recurring.Interval);
            }

            if (lastGenerated != recurring.LastGeneratedAt || next != recurring.NextDueAt)
            {
                var updated = recurring with
                {
                    LastGeneratedAt = lastGenerated,
                    NextDueAt = next,
                };
                await _recurringRepo.UpdateAsync(updated, ct).ConfigureAwait(false);
            }
        }

        return new RecurringRunResult(autoApplied, pendingCreated);
    }

    /// <summary>確認某筆 pending entry：寫入 Trade 並標記為 Confirmed。</summary>
    public async Task<Guid> ConfirmAsync(Guid pendingId, CancellationToken ct = default)
    {
        var entry = await _pendingRepo.GetByIdAsync(pendingId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Pending entry {pendingId} not found.");
        if (entry.Status != PendingStatus.Pending)
            throw new InvalidOperationException($"Pending entry {pendingId} is already {entry.Status}.");

        var trade = new Trade(
            Id: Guid.NewGuid(),
            Symbol: string.Empty,
            Exchange: string.Empty,
            Name: entry.Note ?? string.Empty,
            Type: entry.TradeType,
            TradeDate: entry.DueDate,
            Price: 0m,
            Quantity: 1,
            RealizedPnl: null,
            RealizedPnlPct: null,
            CashAmount: entry.Amount,
            CashAccountId: entry.CashAccountId,
            Note: entry.Note,
            CategoryId: entry.CategoryId,
            RecurringSourceId: entry.RecurringSourceId);

        await _transactionService.RecordAsync(trade).ConfigureAwait(false);

        var resolved = entry with
        {
            Status = PendingStatus.Confirmed,
            GeneratedTradeId = trade.Id,
            ResolvedAt = DateTime.UtcNow,
        };
        await _pendingRepo.UpdateAsync(resolved, ct).ConfigureAwait(false);
        return trade.Id;
    }

    /// <summary>跳過某筆 pending entry：標記為 Skipped，不寫入 Trade。</summary>
    public async Task SkipAsync(Guid pendingId, CancellationToken ct = default)
    {
        var entry = await _pendingRepo.GetByIdAsync(pendingId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Pending entry {pendingId} not found.");
        if (entry.Status != PendingStatus.Pending) return;
        var resolved = entry with
        {
            Status = PendingStatus.Skipped,
            ResolvedAt = DateTime.UtcNow,
        };
        await _pendingRepo.UpdateAsync(resolved, ct).ConfigureAwait(false);
    }

    private static Trade BuildTrade(RecurringTransaction r, DateTime due) => new(
        Id: Guid.NewGuid(),
        Symbol: string.Empty,
        Exchange: string.Empty,
        Name: r.Name,
        Type: r.TradeType,
        TradeDate: due,
        Price: 0m,
        Quantity: 1,
        RealizedPnl: null,
        RealizedPnlPct: null,
        CashAmount: r.Amount,
        CashAccountId: r.CashAccountId,
        Note: r.Note,
        CategoryId: r.CategoryId,
        RecurringSourceId: r.Id);

    private static PendingRecurringEntry BuildPending(RecurringTransaction r, DateTime due) => new(
        Id: Guid.NewGuid(),
        RecurringSourceId: r.Id,
        DueDate: due,
        Amount: r.Amount,
        TradeType: r.TradeType,
        CashAccountId: r.CashAccountId,
        CategoryId: r.CategoryId,
        Note: r.Note ?? r.Name,
        Status: PendingStatus.Pending);

    public static DateTime Advance(DateTime current, RecurrenceFrequency freq, int interval)
    {
        var step = Math.Max(1, interval);
        return freq switch
        {
            RecurrenceFrequency.Daily     => current.AddDays(step),
            RecurrenceFrequency.Weekly    => current.AddDays(7 * step),
            RecurrenceFrequency.BiWeekly  => current.AddDays(14 * step),
            RecurrenceFrequency.Monthly   => current.AddMonths(step),
            RecurrenceFrequency.Quarterly => current.AddMonths(3 * step),
            RecurrenceFrequency.Yearly    => current.AddYears(step),
            _ => current.AddDays(step),
        };
    }
}

public sealed record RecurringRunResult(int AutoApplied, int PendingCreated);
