using Assetra.Application.Portfolio.Dtos;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models;
using Moq;
using Xunit;

namespace Assetra.Tests.Application.Portfolio;

public sealed class FinancialOverviewQueryServiceTests
{
    [Fact]
    public async Task BuildAsync_ConvertsAssetTotalsToBaseCurrency()
    {
        var groupId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var assets = AssetRepo(
            [new AssetGroup(groupId, "Cash", FinancialType.Asset, null, 0, true, Today())],
            [new AssetItem(accountId, "USD Cash", FinancialType.Asset, groupId, "USD", Today())]);
        var balances = BalanceQuery(
            cash: new Dictionary<Guid, decimal> { [accountId] = 100m },
            liabilities: new Dictionary<string, LiabilitySnapshot>());
        var fx = new Mock<IMultiCurrencyValuationService>();
        fx.Setup(x => x.ConvertAsync(100m, "USD", "TWD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3200m);

        var sut = new FinancialOverviewQueryService(
            assets.Object,
            balances.Object,
            fx.Object,
            Settings("TWD").Object);

        var result = await sut.BuildAsync([]);

        Assert.Equal("TWD", result.BaseCurrency);
        Assert.Equal(3200m, result.TotalAssets);
        Assert.Equal(3200m, result.AssetGroups.Single().Subtotal);
        Assert.Equal("TWD", result.AssetGroups.Single().Items.Single().Currency);
        Assert.Equal(3200m, result.AssetGroups.Single().Items.Single().CurrentValue);
    }

    [Fact]
    public async Task BuildAsync_GroupsCreditCardBalancesSeparatelyFromLoans()
    {
        var cardId = Guid.NewGuid();
        var assets = AssetRepo(
            [],
            [new AssetItem(
                cardId,
                "Visa",
                FinancialType.Liability,
                null,
                "TWD",
                Today(),
                LiabilitySubtype: LiabilitySubtype.CreditCard)]);
        var balances = BalanceQuery(
            cash: new Dictionary<Guid, decimal>(),
            liabilities: new Dictionary<string, LiabilitySnapshot>
            {
                ["Visa"] = new(new Money(5000m, "TWD"), new Money(5000m, "TWD")),
            });

        var sut = new FinancialOverviewQueryService(assets.Object, balances.Object);

        var result = await sut.BuildAsync([]);

        var group = Assert.Single(result.LiabilityGroups);
        Assert.Equal("信用卡", group.Name);
        Assert.Equal(5000m, group.Subtotal);
        Assert.Equal(cardId, group.Items.Single().Id);
    }

    [Fact]
    public async Task BuildAsync_ConvertsLiabilityBalancesToBaseCurrency()
    {
        var loanId = Guid.NewGuid();
        var assets = AssetRepo(
            [],
            [new AssetItem(
                loanId,
                "USD Mortgage",
                FinancialType.Liability,
                null,
                "USD",
                Today(),
                LiabilitySubtype: LiabilitySubtype.Loan)]);
        var balances = BalanceQuery(
            cash: new Dictionary<Guid, decimal>(),
            liabilities: new Dictionary<string, LiabilitySnapshot>
            {
                ["USD Mortgage"] = new(new Money(100m, "USD"), new Money(100m, "USD")),
            });
        var fx = new Mock<IMultiCurrencyValuationService>();
        fx.Setup(x => x.ConvertAsync(100m, "USD", "TWD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3200m);

        var sut = new FinancialOverviewQueryService(
            assets.Object,
            balances.Object,
            fx.Object,
            Settings("TWD").Object);

        var result = await sut.BuildAsync([]);

        var group = Assert.Single(result.LiabilityGroups);
        Assert.Equal(3200m, group.Subtotal);
        var item = Assert.Single(group.Items);
        Assert.Equal("TWD", item.Currency);
        Assert.Equal(3200m, item.CurrentValue);
        Assert.Equal(-3200m, result.TotalAssets + result.TotalInvestments - result.TotalLiabilities);
    }

    [Fact]
    public async Task BuildAsync_IncludesConfiguredLiabilityWithZeroBalance()
    {
        var loanId = Guid.NewGuid();
        var assets = AssetRepo(
            [],
            [new AssetItem(
                loanId,
                "Mortgage",
                FinancialType.Liability,
                null,
                "TWD",
                Today(),
                LoanAnnualRate: 0.02m,
                LoanTermMonths: 360,
                LoanStartDate: Today())]);
        var balances = BalanceQuery(
            cash: new Dictionary<Guid, decimal>(),
            liabilities: new Dictionary<string, LiabilitySnapshot>());

        var sut = new FinancialOverviewQueryService(assets.Object, balances.Object);

        var result = await sut.BuildAsync([]);

        var group = Assert.Single(result.LiabilityGroups);
        Assert.Equal("貸款", group.Name);
        Assert.Equal(0m, group.Subtotal);
        var item = Assert.Single(group.Items);
        Assert.Equal(loanId, item.Id);
        Assert.Equal("Mortgage", item.Name);
        Assert.Equal(0m, item.CurrentValue);
    }

    private static Mock<IAssetRepository> AssetRepo(
        IReadOnlyList<AssetGroup> groups,
        IReadOnlyList<AssetItem> items)
    {
        var repo = new Mock<IAssetRepository>();
        repo.Setup(x => x.GetGroupsAsync()).ReturnsAsync(groups);
        repo.Setup(x => x.GetItemsAsync()).ReturnsAsync(items);
        repo.Setup(x => x.GetLatestValuationsAsync(
                It.IsAny<IEnumerable<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, AssetEvent>());
        return repo;
    }

    private static Mock<IBalanceQueryService> BalanceQuery(
        IReadOnlyDictionary<Guid, decimal> cash,
        IReadOnlyDictionary<string, LiabilitySnapshot> liabilities)
    {
        var query = new Mock<IBalanceQueryService>();
        // M1 — wrap legacy decimal fixture into Money tagged TWD; tests don't care
        // about cross-currency cash since they pass empty/USD-tagged liabilities only.
        var cashMoney = cash.ToDictionary(kv => kv.Key, kv => new Money(kv.Value, "TWD"));
        query.Setup(x => x.GetAllCashBalancesAsync())
            .ReturnsAsync((IReadOnlyDictionary<Guid, Money>)cashMoney);
        query.Setup(x => x.GetAllLiabilitySnapshotsAsync()).ReturnsAsync(liabilities);
        return query;
    }

    private static Mock<IAppSettingsService> Settings(string baseCurrency)
    {
        var settings = new Mock<IAppSettingsService>();
        settings.SetupGet(x => x.Current).Returns(new AppSettings(BaseCurrency: baseCurrency));
        return settings;
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Today);
}
