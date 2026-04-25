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

    private sealed class FakeRecurringRepo : IRecurringTransactionRepository
    {
        public List<RecurringTransaction> Store { get; } = [];

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
        public Task<IReadOnlyList<PendingRecurringEntry>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PendingRecurringEntry>>([]);

        public Task<IReadOnlyList<PendingRecurringEntry>> GetByStatusAsync(PendingStatus status, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PendingRecurringEntry>>([]);

        public Task<PendingRecurringEntry?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<PendingRecurringEntry?>(null);

        public Task AddAsync(PendingRecurringEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(PendingRecurringEntry entry, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
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
