using Assetra.Application.Recurring.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Features.Recurring;
using Assetra.WPF.Infrastructure;
using Xunit;

namespace Assetra.Tests.WPF;

public sealed class RecurringViewModelTests
{
    [Fact]
    public async Task AddSubscriptionAsync_PersistsScheduleFields()
    {
        var recurringRepo = new FakeRecurringRepo();
        var pendingRepo = new FakePendingRepo();
        var scheduler = new RecurringTransactionScheduler(
            recurringRepo,
            pendingRepo,
            new FakeTransactionService());
        var startDate = new DateTime(2026, 6, 15);

        var vm = new RecurringViewModel(
            recurringRepo,
            pendingRepo,
            scheduler,
            new FakeSnackbarService(),
            new FakeLocalizationService())
        {
            AddName = "Rent",
            AddTradeType = TradeType.Withdrawal,
            AddAmount = 18000m,
            AddFrequency = RecurrenceFrequency.Monthly,
            AddInterval = 2,
            AddStartDate = startDate,
            AddGenerationMode = AutoGenerationMode.PendingConfirm,
            AddNote = "Lease payment",
        };

        await vm.AddSubscriptionCommand.ExecuteAsync(null);

        var recurring = Assert.Single(recurringRepo.Store);
        Assert.Equal(2, recurring.Interval);
        Assert.Equal(startDate, recurring.StartDate);
        Assert.Equal(startDate, recurring.NextDueAt);
        Assert.Equal("Lease payment", recurring.Note);
    }

    [Fact]
    public async Task AddSubscriptionAsync_RejectsUnsupportedTradeType()
    {
        var recurringRepo = new FakeRecurringRepo();
        var pendingRepo = new FakePendingRepo();
        var scheduler = new RecurringTransactionScheduler(
            recurringRepo,
            pendingRepo,
            new FakeTransactionService());

        var vm = new RecurringViewModel(
            recurringRepo,
            pendingRepo,
            scheduler,
            new FakeSnackbarService(),
            new FakeLocalizationService())
        {
            AddName = "Card bill",
            AddTradeType = TradeType.CreditCardCharge,
            AddAmount = 1200m,
            AddInterval = 1,
            AddStartDate = new DateTime(2026, 4, 1),
        };

        await vm.AddSubscriptionCommand.ExecuteAsync(null);

        Assert.Equal("此交易類型目前不支援訂閱排程", vm.AddError);
        Assert.Empty(recurringRepo.Store);
    }

    [Fact]
    public async Task DeleteSubscriptionAsync_RemovesPendingEntriesFromSameSource()
    {
        var recurringId = Guid.NewGuid();
        var recurring = new RecurringTransaction(
            recurringId,
            "Netflix",
            TradeType.Withdrawal,
            390m,
            null,
            null,
            RecurrenceFrequency.Monthly,
            1,
            new DateTime(2026, 4, 1),
            null,
            AutoGenerationMode.PendingConfirm,
            null,
            new DateTime(2026, 5, 1),
            null,
            true);

        var recurringRepo = new FakeRecurringRepo { Store = [recurring] };
        var pendingRepo = new FakePendingRepo
        {
            Entries =
            [
                new PendingRecurringEntry(Guid.NewGuid(), recurringId, new DateTime(2026, 5, 1), 390m, TradeType.Withdrawal, null, null, null, PendingStatus.Pending, null, null),
                new PendingRecurringEntry(Guid.NewGuid(), Guid.NewGuid(), new DateTime(2026, 5, 2), 120m, TradeType.Withdrawal, null, null, null, PendingStatus.Pending, null, null)
            ]
        };
        var scheduler = new RecurringTransactionScheduler(
            recurringRepo,
            pendingRepo,
            new FakeTransactionService());

        var vm = new RecurringViewModel(
            recurringRepo,
            pendingRepo,
            scheduler,
            new FakeSnackbarService(),
            new FakeLocalizationService());

        await vm.LoadAsync();
        var row = Assert.Single(vm.Subscriptions);

        await vm.DeleteSubscriptionCommand.ExecuteAsync(row);

        Assert.Empty(vm.Subscriptions);
        Assert.Single(vm.Pending);
        Assert.All(vm.Pending, p => Assert.NotEqual(recurringId, p.RecurringSourceId));
        Assert.Equal(recurringId, pendingRepo.RemovedRecurringSourceId);
    }

    private sealed class FakeRecurringRepo : IRecurringTransactionRepository
    {
        public List<RecurringTransaction> Store { get; set; } = [];

        public Task<IReadOnlyList<RecurringTransaction>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RecurringTransaction>>(Store.ToList());

        public Task<IReadOnlyList<RecurringTransaction>> GetActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RecurringTransaction>>(Store.Where(r => r.IsEnabled).ToList());

        public Task<RecurringTransaction?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Store.FirstOrDefault(r => r.Id == id));

        public Task AddAsync(RecurringTransaction recurring, CancellationToken ct = default)
        {
            Store.Add(recurring);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(RecurringTransaction recurring, CancellationToken ct = default)
        {
            var index = Store.FindIndex(r => r.Id == recurring.Id);
            if (index >= 0) Store[index] = recurring;
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
        public List<PendingRecurringEntry> Entries { get; set; } = [];
        public Guid? RemovedRecurringSourceId { get; private set; }

        public Task<IReadOnlyList<PendingRecurringEntry>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PendingRecurringEntry>>(Entries.ToList());

        public Task<IReadOnlyList<PendingRecurringEntry>> GetByStatusAsync(PendingStatus status, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PendingRecurringEntry>>(Entries.Where(x => x.Status == status).ToList());

        public Task<PendingRecurringEntry?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<PendingRecurringEntry?>(Entries.FirstOrDefault(x => x.Id == id));

        public Task AddAsync(PendingRecurringEntry entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(PendingRecurringEntry entry, CancellationToken ct = default)
        {
            var index = Entries.FindIndex(x => x.Id == entry.Id);
            if (index >= 0) Entries[index] = entry;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(Guid id, CancellationToken ct = default)
        {
            Entries.RemoveAll(x => x.Id == id);
            return Task.CompletedTask;
        }

        public Task RemoveByRecurringSourceAsync(Guid recurringSourceId, CancellationToken ct = default)
        {
            RemovedRecurringSourceId = recurringSourceId;
            Entries.RemoveAll(x => x.RecurringSourceId == recurringSourceId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTransactionService : ITransactionService
    {
        public Task RecordAsync(Trade trade) => Task.CompletedTask;
        public Task DeleteAsync(Trade trade) => Task.CompletedTask;
        public Task ReplaceAsync(Trade original, Trade replacement) => Task.CompletedTask;
    }

    private sealed class FakeSnackbarService : ISnackbarService
    {
        public void Show(string message, SnackbarKind kind = SnackbarKind.Info) { }
        public void Success(string message) { }
        public void Warning(string message) { }
        public void Error(string message) { }
    }

    private sealed class FakeLocalizationService : ILocalizationService
    {
        public string CurrentLanguage => "zh-TW";
        public event EventHandler? LanguageChanged;
        public string Get(string key, string fallback = "") => fallback;
        public void SetLanguage(string languageCode) => LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
