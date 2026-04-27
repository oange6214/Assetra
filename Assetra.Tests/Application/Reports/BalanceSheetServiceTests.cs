using Assetra.Application.Reports.Statements;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Application.Reports;

public class BalanceSheetServiceTests
{
    [Fact]
    public async Task GenerateAsync_ComputesCashAssetBalance_FromTradeJournal()
    {
        var bankId = Guid.NewGuid();
        var assets = new FakeAssetRepo();
        assets.Store.Add(new AssetItem(bankId, "Bank", FinancialType.Asset, null, "TWD", new DateOnly(2026, 1, 1)));

        var trades = new FakeTradeRepo();
        trades.Store.Add(MakeIncome(new DateTime(2026, 4, 1), 50000m, bankId));
        trades.Store.Add(MakeWithdrawal(new DateTime(2026, 4, 2), 5000m, bankId));

        var svc = new BalanceSheetService(assets, trades);
        var bs = await svc.GenerateAsync(new DateOnly(2026, 4, 30));

        Assert.Equal(45000m, bs.Assets.Total);
        Assert.Equal(0m, bs.Liabilities.Total);
        Assert.Equal(45000m, bs.NetWorth);
    }

    [Fact]
    public async Task GenerateAsync_OnlyCountsTradesUntilAsOf()
    {
        var bankId = Guid.NewGuid();
        var assets = new FakeAssetRepo();
        assets.Store.Add(new AssetItem(bankId, "Bank", FinancialType.Asset, null, "TWD", new DateOnly(2026, 1, 1)));

        var trades = new FakeTradeRepo();
        trades.Store.Add(MakeIncome(new DateTime(2026, 4, 1), 1000m, bankId));
        trades.Store.Add(MakeIncome(new DateTime(2026, 5, 1), 9999m, bankId)); // after AsOf

        var svc = new BalanceSheetService(assets, trades);
        var bs = await svc.GenerateAsync(new DateOnly(2026, 4, 30));

        Assert.Equal(1000m, bs.Assets.Total);
    }

    private static Trade MakeIncome(DateTime when, decimal amount, Guid accountId) => new(
        Guid.NewGuid(), string.Empty, string.Empty, "income",
        TradeType.Income, when, 0m, 1, null, null,
        CashAmount: amount, CashAccountId: accountId);

    private static Trade MakeWithdrawal(DateTime when, decimal amount, Guid accountId) => new(
        Guid.NewGuid(), string.Empty, string.Empty, "expense",
        TradeType.Withdrawal, when, 0m, 1, null, null,
        CashAmount: amount, CashAccountId: accountId);

    private sealed class FakeTradeRepo : ITradeRepository
    {
        public List<Trade> Store { get; } = new();
        public Task<IReadOnlyList<Trade>> GetAllAsync() => Task.FromResult<IReadOnlyList<Trade>>(Store.ToList());
        public Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string l) => Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid id) =>
            Task.FromResult<IReadOnlyList<Trade>>(Store.Where(t => t.CashAccountId == id).ToList());
        public Task<Trade?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<Trade?>(Store.FirstOrDefault(t => t.Id == id));
        public Task AddAsync(Trade t) { Store.Add(t); return Task.CompletedTask; }
        public Task UpdateAsync(Trade t) => Task.CompletedTask;
        public Task RemoveAsync(Guid id) => Task.CompletedTask;
        public Task RemoveChildrenAsync(Guid id) => Task.CompletedTask;
        public Task RemoveByAccountIdAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByLiabilityAsync(Guid? id, string? l, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeAssetRepo : IAssetRepository
    {
        public List<AssetItem> Store { get; } = new();
        public Task<IReadOnlyList<AssetItem>> GetItemsAsync() => Task.FromResult<IReadOnlyList<AssetItem>>(Store.ToList());
        public Task<IReadOnlyList<AssetItem>> GetItemsByTypeAsync(FinancialType type) =>
            Task.FromResult<IReadOnlyList<AssetItem>>(Store.Where(a => a.Type == type).ToList());
        public Task<AssetItem?> GetByIdAsync(Guid id) => Task.FromResult(Store.FirstOrDefault(a => a.Id == id));
        public Task AddItemAsync(AssetItem item) { Store.Add(item); return Task.CompletedTask; }
        public Task UpdateItemAsync(AssetItem item) => Task.CompletedTask;
        public Task DeleteItemAsync(Guid id) => Task.CompletedTask;
        public Task<Guid> FindOrCreateAccountAsync(string n, string c, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid());
        public Task ArchiveItemAsync(Guid id) => Task.CompletedTask;
        public Task<int> HasTradeReferencesAsync(Guid id, CancellationToken ct = default) => Task.FromResult(0);
        public Task<IReadOnlyList<AssetGroup>> GetGroupsAsync() => Task.FromResult<IReadOnlyList<AssetGroup>>([]);
        public Task AddGroupAsync(AssetGroup g) => Task.CompletedTask;
        public Task UpdateGroupAsync(AssetGroup g) => Task.CompletedTask;
        public Task DeleteGroupAsync(Guid id) => Task.CompletedTask;
        public Task<IReadOnlyList<AssetEvent>> GetEventsAsync(Guid id) => Task.FromResult<IReadOnlyList<AssetEvent>>([]);
        public Task AddEventAsync(AssetEvent e) => Task.CompletedTask;
        public Task DeleteEventAsync(Guid id) => Task.CompletedTask;
        public Task<AssetEvent?> GetLatestValuationAsync(Guid id) => Task.FromResult<AssetEvent?>(null);
    }
}
