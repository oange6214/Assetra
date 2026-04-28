using Assetra.Application.Recurring.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Application;

public class RecurringTransactionSchedulerTests
{
    [Fact]
    public async Task RunAsync_AutoApply_RecordsTradesAndAdvancesNextDue()
    {
        var recurring = new FakeRecurringRepo();
        var pending = new FakePendingRepo();
        var tx = new FakeTransactionService();

        var sub = new RecurringTransaction(
            Id: Guid.NewGuid(),
            Name: "Rent",
            TradeType: TradeType.Withdrawal,
            Amount: 18000m,
            CashAccountId: Guid.NewGuid(),
            CategoryId: null,
            Frequency: RecurrenceFrequency.Monthly,
            Interval: 1,
            StartDate: new DateTime(2026, 1, 1),
            EndDate: null,
            GenerationMode: AutoGenerationMode.AutoApply,
            NextDueAt: new DateTime(2026, 1, 1));
        recurring.Store.Add(sub);

        var scheduler = new RecurringTransactionScheduler(recurring, pending, tx);
        var result = await scheduler.RunAsync(new DateTime(2026, 4, 15));

        // Should have generated trades for Jan, Feb, Mar, Apr (4 runs)
        Assert.Equal(4, result.AutoApplied);
        Assert.Equal(0, result.PendingCreated);
        Assert.Equal(4, tx.Recorded.Count);
        Assert.All(tx.Recorded, t => Assert.Equal(TradeType.Withdrawal, t.Type));
        Assert.All(tx.Recorded, t => Assert.Equal(sub.Id, t.RecurringSourceId));

        var updated = recurring.Store[0];
        Assert.Equal(new DateTime(2026, 4, 1), updated.LastGeneratedAt);
        Assert.Equal(new DateTime(2026, 5, 1), updated.NextDueAt);
    }

    [Fact]
    public async Task RunAsync_PendingConfirm_CreatesPendingEntries()
    {
        var recurring = new FakeRecurringRepo();
        var pending = new FakePendingRepo();
        var tx = new FakeTransactionService();

        recurring.Store.Add(new RecurringTransaction(
            Id: Guid.NewGuid(),
            Name: "Netflix",
            TradeType: TradeType.Withdrawal,
            Amount: 390m,
            CashAccountId: null,
            CategoryId: null,
            Frequency: RecurrenceFrequency.Monthly,
            Interval: 1,
            StartDate: new DateTime(2026, 3, 10),
            EndDate: null,
            GenerationMode: AutoGenerationMode.PendingConfirm,
            NextDueAt: new DateTime(2026, 3, 10)));

        var scheduler = new RecurringTransactionScheduler(recurring, pending, tx);
        var result = await scheduler.RunAsync(new DateTime(2026, 4, 15));

        Assert.Equal(0, result.AutoApplied);
        Assert.Equal(2, result.PendingCreated);
        Assert.Empty(tx.Recorded);
        Assert.Equal(2, pending.Store.Count);
        Assert.All(pending.Store, p => Assert.Equal(PendingStatus.Pending, p.Status));
    }

    [Fact]
    public async Task RunAsync_RespectsEndDate()
    {
        var recurring = new FakeRecurringRepo();
        var pending = new FakePendingRepo();
        var tx = new FakeTransactionService();

        recurring.Store.Add(new RecurringTransaction(
            Id: Guid.NewGuid(),
            Name: "Trial",
            TradeType: TradeType.Withdrawal,
            Amount: 100m,
            CashAccountId: null,
            CategoryId: null,
            Frequency: RecurrenceFrequency.Monthly,
            Interval: 1,
            StartDate: new DateTime(2026, 1, 1),
            EndDate: new DateTime(2026, 2, 15),
            GenerationMode: AutoGenerationMode.AutoApply,
            NextDueAt: new DateTime(2026, 1, 1)));

        var scheduler = new RecurringTransactionScheduler(recurring, pending, tx);
        var result = await scheduler.RunAsync(new DateTime(2026, 6, 1));

        // Jan 1 + Feb 1, Mar 1 is past EndDate
        Assert.Equal(2, result.AutoApplied);
    }

    [Fact]
    public async Task RunAsync_SkipsDisabled()
    {
        var recurring = new FakeRecurringRepo();
        var pending = new FakePendingRepo();
        var tx = new FakeTransactionService();

        recurring.Store.Add(new RecurringTransaction(
            Id: Guid.NewGuid(), Name: "Off", TradeType: TradeType.Withdrawal,
            Amount: 100m, CashAccountId: null, CategoryId: null,
            Frequency: RecurrenceFrequency.Monthly, Interval: 1,
            StartDate: new DateTime(2026, 1, 1), EndDate: null,
            GenerationMode: AutoGenerationMode.AutoApply,
            NextDueAt: new DateTime(2026, 1, 1),
            IsEnabled: false));

        var scheduler = new RecurringTransactionScheduler(recurring, pending, tx);
        var result = await scheduler.RunAsync(new DateTime(2026, 6, 1));

        Assert.Equal(0, result.AutoApplied);
    }

    [Fact]
    public async Task ConfirmAsync_RecordsTrade_AndMarksConfirmed()
    {
        var recurring = new FakeRecurringRepo();
        var pending = new FakePendingRepo();
        var tx = new FakeTransactionService();

        var entry = new PendingRecurringEntry(
            Id: Guid.NewGuid(),
            RecurringSourceId: Guid.NewGuid(),
            DueDate: new DateTime(2026, 4, 1),
            Amount: 390m,
            TradeType: TradeType.Withdrawal,
            CashAccountId: Guid.NewGuid(),
            CategoryId: null,
            Note: "Netflix");
        pending.Store.Add(entry);

        var scheduler = new RecurringTransactionScheduler(recurring, pending, tx);
        var tradeId = await scheduler.ConfirmAsync(entry.Id);

        Assert.Single(tx.Recorded);
        Assert.Equal(390m, tx.Recorded[0].CashAmount);
        var resolved = pending.Store[0];
        Assert.Equal(PendingStatus.Confirmed, resolved.Status);
        Assert.Equal(tradeId, resolved.GeneratedTradeId);
    }

    [Fact]
    public async Task SkipAsync_MarksSkipped_NoTradeRecorded()
    {
        var recurring = new FakeRecurringRepo();
        var pending = new FakePendingRepo();
        var tx = new FakeTransactionService();

        var entry = new PendingRecurringEntry(
            Id: Guid.NewGuid(), RecurringSourceId: Guid.NewGuid(),
            DueDate: new DateTime(2026, 4, 1), Amount: 100m,
            TradeType: TradeType.Withdrawal, CashAccountId: null,
            CategoryId: null, Note: null);
        pending.Store.Add(entry);

        var scheduler = new RecurringTransactionScheduler(recurring, pending, tx);
        await scheduler.SkipAsync(entry.Id);

        Assert.Empty(tx.Recorded);
        Assert.Equal(PendingStatus.Skipped, pending.Store[0].Status);
    }

    private sealed class FakeRecurringRepo : IRecurringTransactionRepository
    {
        public List<RecurringTransaction> Store { get; } = new();
        public Task<IReadOnlyList<RecurringTransaction>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RecurringTransaction>>(Store.ToList());
        public Task<IReadOnlyList<RecurringTransaction>> GetActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RecurringTransaction>>(Store.Where(r => r.IsEnabled).ToList());
        public Task<RecurringTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Store.FirstOrDefault(r => r.Id == id));
        public Task AddAsync(RecurringTransaction r, CancellationToken ct = default) { Store.Add(r); return Task.CompletedTask; }
        public Task UpdateAsync(RecurringTransaction r, CancellationToken ct = default)
        {
            var idx = Store.FindIndex(x => x.Id == r.Id);
            if (idx >= 0) Store[idx] = r;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(Guid id, CancellationToken ct = default)
        {
            Store.RemoveAll(r => r.Id == id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePendingRepo : IPendingRecurringEntryRepository
    {
        public List<PendingRecurringEntry> Store { get; } = new();
        public Task<IReadOnlyList<PendingRecurringEntry>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PendingRecurringEntry>>(Store.ToList());
        public Task<IReadOnlyList<PendingRecurringEntry>> GetByStatusAsync(PendingStatus status, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PendingRecurringEntry>>(Store.Where(e => e.Status == status).ToList());
        public Task<PendingRecurringEntry?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Store.FirstOrDefault(e => e.Id == id));
        public Task AddAsync(PendingRecurringEntry e, CancellationToken ct = default) { Store.Add(e); return Task.CompletedTask; }
        public Task UpdateAsync(PendingRecurringEntry e, CancellationToken ct = default)
        {
            var idx = Store.FindIndex(x => x.Id == e.Id);
            if (idx >= 0) Store[idx] = e;
            return Task.CompletedTask;
        }
        public Task RemoveAsync(Guid id, CancellationToken ct = default)
        {
            Store.RemoveAll(e => e.Id == id);
            return Task.CompletedTask;
        }
        public Task RemoveByRecurringSourceAsync(Guid recurringSourceId, CancellationToken ct = default)
        {
            Store.RemoveAll(e => e.RecurringSourceId == recurringSourceId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTransactionService : ITransactionService
    {
        public List<Trade> Recorded { get; } = new();
        public Task RecordAsync(Trade trade) { Recorded.Add(trade); return Task.CompletedTask; }
        public Task DeleteAsync(Trade trade) { Recorded.RemoveAll(t => t.Id == trade.Id); return Task.CompletedTask; }
        public Task ReplaceAsync(Trade original, Trade replacement)
        {
            var idx = Recorded.FindIndex(t => t.Id == original.Id);
            if (idx >= 0) Recorded[idx] = replacement;
            return Task.CompletedTask;
        }
    }
}
