using Assetra.Application.Reports.Statements;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;
using Xunit;

namespace Assetra.Tests.Application.MultiAsset;

public class BalanceSheetMultiAssetTests
{
    private static readonly DateOnly AsOf = new(2026, 4, 28);

    private static RealEstate MakeProperty(decimal currentValue, decimal mortgage = 0m) =>
        new(Guid.NewGuid(), "House", "台北市", 8_000_000m,
            new DateOnly(2020, 1, 1), currentValue, mortgage, "TWD",
            false, RealEstateStatus.Active, null, new EntityVersion());

    private static InsurancePolicy MakePolicy(decimal cashValue, string currency = "TWD") =>
        new(Guid.NewGuid(), "Life", "P001", InsuranceType.WholeLife, "國泰",
            new DateOnly(2020, 1, 1), null, 2_000_000m, cashValue, 24_000m,
            currency, InsurancePolicyStatus.Active, null, new EntityVersion());

    [Fact]
    public async Task GenerateAsync_WithRealEstate_IncludesEquityRow()
    {
        var prop = MakeProperty(10_000_000m, mortgage: 4_000_000m);
        var svc = new BalanceSheetService(
            new StubAssetRepo(),
            new StubTradeRepo(),
            realEstate: new StubRealEstateRepo([prop]));

        var sheet = await svc.GenerateAsync(AsOf);

        var realEstateRow = sheet.Assets.Rows.FirstOrDefault(r => r.Group == "Real Estate");
        Assert.NotNull(realEstateRow);
        Assert.Equal(6_000_000m, realEstateRow.Amount); // equity = 10M - 4M
    }

    [Fact]
    public async Task GenerateAsync_WithInsurance_IncludesCashValueRow()
    {
        var policy = MakePolicy(250_000m);
        var svc = new BalanceSheetService(
            new StubAssetRepo(),
            new StubTradeRepo(),
            insurancePolicies: new StubInsuranceRepo([policy]));

        var sheet = await svc.GenerateAsync(AsOf);

        var insuranceRow = sheet.Assets.Rows.FirstOrDefault(r => r.Group == "Insurance");
        Assert.NotNull(insuranceRow);
        Assert.Equal(250_000m, insuranceRow.Amount);
    }

    [Fact]
    public async Task GenerateAsync_SoldProperty_NotIncluded()
    {
        var sold = new RealEstate(Guid.NewGuid(), "Sold", "台北市", 5_000_000m,
            new DateOnly(2018, 1, 1), 5_500_000m, 0m, "TWD",
            false, RealEstateStatus.Sold, null, new EntityVersion());
        var svc = new BalanceSheetService(
            new StubAssetRepo(),
            new StubTradeRepo(),
            realEstate: new StubRealEstateRepo([sold]));

        var sheet = await svc.GenerateAsync(AsOf);

        Assert.DoesNotContain(sheet.Assets.Rows, r => r.Group == "Real Estate");
    }

    [Fact]
    public async Task GenerateAsync_LapsedPolicy_NotIncluded()
    {
        var lapsed = new InsurancePolicy(Guid.NewGuid(), "Lapsed", "P002", InsuranceType.TermLife,
            "富邦", new DateOnly(2015, 1, 1), null, 1_000_000m, 5_000m, 10_000m,
            "TWD", InsurancePolicyStatus.Lapsed, null, new EntityVersion());
        var svc = new BalanceSheetService(
            new StubAssetRepo(),
            new StubTradeRepo(),
            insurancePolicies: new StubInsuranceRepo([lapsed]));

        var sheet = await svc.GenerateAsync(AsOf);

        Assert.DoesNotContain(sheet.Assets.Rows, r => r.Group == "Insurance");
    }

    [Fact]
    public async Task GenerateAsync_NetWorthIncludesRealEstateEquity()
    {
        var prop = MakeProperty(8_000_000m, mortgage: 3_000_000m);
        var policy = MakePolicy(100_000m);
        var svc = new BalanceSheetService(
            new StubAssetRepo(),
            new StubTradeRepo(),
            realEstate: new StubRealEstateRepo([prop]),
            insurancePolicies: new StubInsuranceRepo([policy]));

        var sheet = await svc.GenerateAsync(AsOf);

        // equity=5M + cashValue=100K
        Assert.Equal(5_100_000m, sheet.NetWorth);
    }

    [Fact]
    public async Task GenerateAsync_ConvertsForeignInsuranceRetirementAndPhysicalAssets()
    {
        var policy = MakePolicy(1000m, "USD");
        var retirement = new RetirementAccount(
            Guid.NewGuid(), "IRA", RetirementAccountType.IndividualRetirementAccount, "Broker",
            2000m, 0m, 0m, 5, 65, new DateOnly(2020, 1, 1),
            "USD", RetirementAccountStatus.Active, null, new EntityVersion());
        var physical = new PhysicalAsset(
            Guid.NewGuid(), "Gold", PhysicalAssetCategory.PreciousMetal, "coin",
            300m, new DateOnly(2020, 1, 1), 500m, "market",
            "USD", PhysicalAssetStatus.Active, null, new EntityVersion());
        var svc = new BalanceSheetService(
            new StubAssetRepo(),
            new StubTradeRepo(),
            fx: new StubFx(new Dictionary<(string, string), decimal>
            {
                { ("USD", "TWD"), 30m },
            }),
            settings: new StubSettings("TWD"),
            insurancePolicies: new StubInsuranceRepo([policy]),
            retirementAccounts: new StubRetirementRepo([retirement]),
            physicalAssets: new StubPhysicalAssetRepo([physical]));

        var sheet = await svc.GenerateAsync(AsOf);

        Assert.Equal(105000m, sheet.Assets.Total);
        Assert.Equal(30000m, sheet.Assets.Rows.Single(r => r.Group == "Insurance").Amount);
        Assert.Equal(60000m, sheet.Assets.Rows.Single(r => r.Group == "Retirement").Amount);
        Assert.Equal(15000m, sheet.Assets.Rows.Single(r => r.Group == "Physical Asset").Amount);
    }

    // ── Stubs ──

    private sealed class StubAssetRepo : IAssetRepository
    {
        public Task<IReadOnlyList<AssetGroup>> GetGroupsAsync() => Task.FromResult<IReadOnlyList<AssetGroup>>([]);
        public Task AddGroupAsync(AssetGroup group) => Task.CompletedTask;
        public Task UpdateGroupAsync(AssetGroup group) => Task.CompletedTask;
        public Task DeleteGroupAsync(Guid id) => Task.CompletedTask;
        public Task<IReadOnlyList<AssetItem>> GetItemsAsync() => Task.FromResult<IReadOnlyList<AssetItem>>([]);
        public Task<IReadOnlyList<AssetItem>> GetItemsByTypeAsync(FinancialType type) => Task.FromResult<IReadOnlyList<AssetItem>>([]);
        public Task<AssetItem?> GetByIdAsync(Guid id) => Task.FromResult<AssetItem?>(null);
        public Task AddItemAsync(AssetItem item) => Task.CompletedTask;
        public Task UpdateItemAsync(AssetItem item) => Task.CompletedTask;
        public Task DeleteItemAsync(Guid id) => Task.CompletedTask;
        public Task<Guid> FindOrCreateAccountAsync(string name, string currency, CancellationToken ct = default) => Task.FromResult(Guid.NewGuid());
        public Task ArchiveItemAsync(Guid id) => Task.CompletedTask;
        public Task<int> HasTradeReferencesAsync(Guid id, CancellationToken ct = default) => Task.FromResult(0);
        public Task<IReadOnlyList<AssetEvent>> GetEventsAsync(Guid assetId) => Task.FromResult<IReadOnlyList<AssetEvent>>([]);
        public Task AddEventAsync(AssetEvent evt) => Task.CompletedTask;
        public Task DeleteEventAsync(Guid id) => Task.CompletedTask;
        public Task<AssetEvent?> GetLatestValuationAsync(Guid assetId) => Task.FromResult<AssetEvent?>(null);
    }

    private sealed class StubTradeRepo : ITradeRepository
    {
        public Task<IReadOnlyList<Trade>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid cashAccountId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string loanLabel, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Trade>>([]);
        public Task<Trade?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Trade?>(null);
        public Task AddAsync(Trade trade, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(Trade trade, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveChildrenAsync(Guid parentId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByAccountIdAsync(Guid accountId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveByLiabilityAsync(Guid? liabilityAssetId, string? loanLabel, CancellationToken ct = default) => Task.CompletedTask;
        public Task ApplyAtomicAsync(IReadOnlyList<TradeMutation> mutations, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubRealEstateRepo(IReadOnlyList<RealEstate> data) : IRealEstateRepository
    {
        public Task<IReadOnlyList<RealEstate>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(data);
        public Task<RealEstate?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(data.FirstOrDefault(x => x.Id == id));
        public Task AddAsync(RealEstate entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(RealEstate entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubInsuranceRepo(IReadOnlyList<InsurancePolicy> data) : IInsurancePolicyRepository
    {
        public Task<IReadOnlyList<InsurancePolicy>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(data);
        public Task<InsurancePolicy?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(data.FirstOrDefault(x => x.Id == id));
        public Task AddAsync(InsurancePolicy policy, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(InsurancePolicy policy, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubRetirementRepo(IReadOnlyList<RetirementAccount> data) : IRetirementAccountRepository
    {
        public Task<IReadOnlyList<RetirementAccount>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(data);
        public Task<RetirementAccount?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(data.FirstOrDefault(x => x.Id == id));
        public Task AddAsync(RetirementAccount entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(RetirementAccount entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubPhysicalAssetRepo(IReadOnlyList<PhysicalAsset> data) : IPhysicalAssetRepository
    {
        public Task<IReadOnlyList<PhysicalAsset>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(data);
        public Task<PhysicalAsset?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(data.FirstOrDefault(x => x.Id == id));
        public Task AddAsync(PhysicalAsset entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(PhysicalAsset entity, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubFx(Dictionary<(string, string), decimal> rates) : IMultiCurrencyValuationService
    {
        public Task<decimal?> ConvertAsync(decimal amount, string from, string to, DateOnly asOf, CancellationToken ct = default)
        {
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<decimal?>(amount);
            return rates.TryGetValue((from.ToUpperInvariant(), to.ToUpperInvariant()), out var rate)
                ? Task.FromResult<decimal?>(amount * rate)
                : Task.FromResult<decimal?>(null);
        }
    }

    private sealed class StubSettings(string baseCurrency) : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = new(BaseCurrency: baseCurrency);

        public event Action? Changed;

        public Task SaveAsync(AppSettings settings)
        {
            Current = settings;
            Changed?.Invoke();
            return Task.CompletedTask;
        }
    }
}
