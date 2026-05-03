using Assetra.Application.Reports.Statements;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;
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

    [Fact]
    public async Task GenerateAsync_ConvertsForeignCurrencyToBaseCurrency()
    {
        var usdBankId = Guid.NewGuid();
        var assets = new FakeAssetRepo();
        assets.Store.Add(new AssetItem(usdBankId, "USD Bank", FinancialType.Asset, null, "USD", new DateOnly(2026, 1, 1)));

        var trades = new FakeTradeRepo();
        trades.Store.Add(MakeIncome(new DateTime(2026, 4, 1), 1000m, usdBankId));

        var fx = new StubFx(new Dictionary<(string, string), decimal>
        {
            { ("USD", "TWD"), 32m },
        });
        var svc = new BalanceSheetService(assets, trades, snapshots: null, fx: fx, settings: new FakeSettings("TWD"));
        var bs = await svc.GenerateAsync(new DateOnly(2026, 4, 30));

        Assert.Equal(32000m, bs.Assets.Total);
        Assert.Equal(32000m, bs.NetWorth);
    }

    [Fact]
    public async Task GenerateAsync_NoFxRate_KeepsOriginalAmount()
    {
        var usdBankId = Guid.NewGuid();
        var assets = new FakeAssetRepo();
        assets.Store.Add(new AssetItem(usdBankId, "USD Bank", FinancialType.Asset, null, "USD", new DateOnly(2026, 1, 1)));

        var trades = new FakeTradeRepo();
        trades.Store.Add(MakeIncome(new DateTime(2026, 4, 1), 1000m, usdBankId));

        var fx = new StubFx(new Dictionary<(string, string), decimal>());
        var svc = new BalanceSheetService(assets, trades, snapshots: null, fx: fx, settings: new FakeSettings("TWD"));
        var bs = await svc.GenerateAsync(new DateOnly(2026, 4, 30));

        Assert.Equal(1000m, bs.Assets.Total);
    }

    [Fact]
    public async Task GenerateAsync_ConvertsPortfolioSnapshotToBaseCurrency()
    {
        var assets = new FakeAssetRepo();
        var trades = new FakeTradeRepo();
        var snapshots = new FakeSnapshotRepo();
        snapshots.Store.Add(new PortfolioDailySnapshot(
            new DateOnly(2026, 4, 30),
            TotalCost: 900m,
            MarketValue: 1000m,
            Pnl: 100m,
            PositionCount: 1,
            Currency: "USD"));

        var fx = new StubFx(new Dictionary<(string, string), decimal>
        {
            { ("USD", "TWD"), 32m },
        });
        var svc = new BalanceSheetService(assets, trades, snapshots, fx, new FakeSettings("TWD"));
        var bs = await svc.GenerateAsync(new DateOnly(2026, 4, 30));

        Assert.Equal(32000m, bs.Assets.Total);
    }

    [Fact]
    public async Task GenerateAsync_RealEstatePurchasedAfterAsOf_IsNotIncluded()
    {
        var property = new RealEstate(
            Guid.NewGuid(),
            "Future House",
            "Taipei",
            8_000_000m,
            new DateOnly(2026, 5, 1),
            10_000_000m,
            2_000_000m,
            "TWD",
            false,
            RealEstateStatus.Active,
            null,
            new EntityVersion());

        var svc = new BalanceSheetService(
            new FakeAssetRepo(),
            new FakeTradeRepo(),
            realEstate: new FakeRealEstateRepo([property]));

        var bs = await svc.GenerateAsync(new DateOnly(2026, 4, 30));

        Assert.DoesNotContain(bs.Assets.Rows, r => r.Group == "Real Estate");
        Assert.Equal(0m, bs.Assets.Total);
    }

    [Fact]
    public async Task GenerateAsync_ConvertsForeignRealEstateToBaseCurrency()
    {
        var property = new RealEstate(
            Guid.NewGuid(),
            "USD House",
            "New York",
            1000m,
            new DateOnly(2020, 1, 1),
            1000m,
            250m,
            "USD",
            false,
            RealEstateStatus.Active,
            null,
            new EntityVersion());
        var fx = new StubFx(new Dictionary<(string, string), decimal>
        {
            { ("USD", "TWD"), 32m },
        });
        var svc = new BalanceSheetService(
            new FakeAssetRepo(),
            new FakeTradeRepo(),
            snapshots: null,
            fx: fx,
            settings: new FakeSettings("TWD"),
            realEstate: new FakeRealEstateRepo([property]));

        var bs = await svc.GenerateAsync(new DateOnly(2026, 4, 30));

        Assert.Equal(24000m, bs.Assets.Total);
        Assert.Equal(24000m, bs.Assets.Rows.Single(r => r.Group == "Real Estate").Amount);
    }

    private sealed class StubFx : IMultiCurrencyValuationService
    {
        private readonly Dictionary<(string, string), decimal> _rates;
        public StubFx(Dictionary<(string, string), decimal> rates) { _rates = rates; }
        public Task<decimal?> ConvertAsync(decimal amount, string from, string to, DateOnly asOf, CancellationToken ct = default)
        {
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<decimal?>(amount);
            if (_rates.TryGetValue((from.ToUpperInvariant(), to.ToUpperInvariant()), out var rate))
                return Task.FromResult<decimal?>(amount * rate);
            return Task.FromResult<decimal?>(null);
        }
    }

    private sealed class FakeSettings : IAppSettingsService
    {
        public FakeSettings(string baseCurrency)
        {
            Current = new AppSettings(BaseCurrency: baseCurrency);
        }

        public AppSettings Current { get; private set; }

        public event Action? Changed;

        public Task SaveAsync(AppSettings settings)
        {
            Current = settings;
            Changed?.Invoke();
            return Task.CompletedTask;
        }
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
        public Task<IReadOnlyList<Trade>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Trade>>(Store.ToList());
        public Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string l, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Trade>>(Store.Where(t => t.CashAccountId == id).ToList());
        public Task<Trade?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<Trade?>(Store.FirstOrDefault(t => t.Id == id));
        public Task AddAsync(Trade t, CancellationToken ct = default) { Store.Add(t); return Task.CompletedTask; }
        public Task UpdateAsync(Trade t, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveChildrenAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByAccountIdAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByLiabilityAsync(Guid? id, string? l, CancellationToken ct = default) => Task.CompletedTask;
        public Task ApplyAtomicAsync(IReadOnlyList<TradeMutation> mutations, CancellationToken ct = default)
        {
            foreach (var m in mutations)
            {
                switch (m)
                {
                    case AddTradeMutation add: Store.Add(add.Trade); break;
                    case RemoveTradeMutation rem: Store.RemoveAll(t => t.Id == rem.Id); break;
                }
            }
            return Task.CompletedTask;
        }
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

    private sealed class FakeSnapshotRepo : IPortfolioSnapshotRepository
    {
        public List<PortfolioDailySnapshot> Store { get; } = new();

        public Task<IReadOnlyList<PortfolioDailySnapshot>> GetSnapshotsAsync(
            DateOnly? from = null,
            DateOnly? to = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PortfolioDailySnapshot>>(Store
                .Where(s => (!from.HasValue || s.SnapshotDate >= from.Value)
                            && (!to.HasValue || s.SnapshotDate <= to.Value))
                .ToList());

        public Task<PortfolioDailySnapshot?> GetSnapshotAsync(DateOnly date, CancellationToken ct = default) =>
            Task.FromResult(Store.FirstOrDefault(s => s.SnapshotDate == date));

        public Task UpsertAsync(PortfolioDailySnapshot snapshot, CancellationToken ct = default)
        {
            Store.RemoveAll(s => s.SnapshotDate == snapshot.SnapshotDate);
            Store.Add(snapshot);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRealEstateRepo(IReadOnlyList<RealEstate> data) : IRealEstateRepository
    {
        public Task<IReadOnlyList<RealEstate>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(data);
        public Task<RealEstate?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(data.FirstOrDefault(x => x.Id == id));
        public Task AddAsync(RealEstate entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(RealEstate entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }
}
