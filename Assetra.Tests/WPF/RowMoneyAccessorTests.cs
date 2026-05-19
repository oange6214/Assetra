using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.WPF.Features.Portfolio;
using Assetra.WPF.Infrastructure.Converters;
using System.Globalization;
using Xunit;

namespace Assetra.Tests.WPF;

/// <summary>
/// M1 (row VM Money accessors) — verifies that BalanceAsMoney /
/// MarketValueAsMoney etc. carry the correct currency tag.
/// </summary>
public class RowMoneyAccessorTests
{
    [Fact]
    public void CashAccountRow_BalanceAsMoney_TagsAccountCurrency()
    {
        var asset = new AssetItem(
            Guid.NewGuid(), "USD Bank", FinancialType.Asset, null, "USD",
            DateOnly.FromDateTime(DateTime.Today));
        var row = new CashAccountRowViewModel(asset, 1234m);

        Assert.Equal(new Money(1234m, "USD"), row.BalanceAsMoney);
    }

    [Fact]
    public void CashAccountRow_BalanceAsMoney_FallsBackToTwdWhenCurrencyBlank()
    {
        var asset = new AssetItem(
            Guid.NewGuid(), "Legacy", FinancialType.Asset, null, string.Empty,
            DateOnly.FromDateTime(DateTime.Today));
        var row = new CashAccountRowViewModel(asset, 100m);

        Assert.Equal("TWD", row.BalanceAsMoney.Currency);
    }

    [Fact]
    public void LiabilityRow_BalanceAsMoney_TagsAssetCurrency()
    {
        var asset = new AssetItem(
            Guid.NewGuid(), "USD Mortgage", FinancialType.Liability, null, "USD",
            DateOnly.FromDateTime(DateTime.Today),
            LoanAnnualRate: 0.03m, LoanTermMonths: 360, LoanStartDate: DateOnly.FromDateTime(DateTime.Today));
        var snapshot = new LiabilitySnapshot(new Money(500m, "USD"), new Money(800m, "USD"));
        var row = new LiabilityRowViewModel("USD Mortgage", snapshot, asset);

        Assert.Equal(new Money(500m, "USD"), row.BalanceAsMoney);
        Assert.Equal(new Money(800m, "USD"), row.OriginalAmountAsMoney);
    }

    [Fact]
    public void LiabilityRow_BalanceAsMoney_DefaultsToTwdWhenNoAsset()
    {
        var snapshot = new LiabilitySnapshot(new Money(100m, "TWD"), new Money(100m, "TWD"));
        var row = new LiabilityRowViewModel("legacy label", snapshot, asset: null);

        Assert.Equal("TWD", row.BalanceAsMoney.Currency);
    }

    [Fact]
    public void PortfolioRow_MarketValueAsMoney_FollowsCurrencyChange()
    {
        var row = new PortfolioRowViewModel { Currency = "USD", MarketValue = 200m, Cost = 150m, Pnl = 50m };

        Assert.Equal(new Money(0m, "USD"), row.BuyPriceAsMoney);
        Assert.Equal(new Money(0m, "USD"), row.CurrentPriceAsMoney);
        Assert.Equal(new Money(200m, "USD"), row.MarketValueAsMoney);
        Assert.Equal(new Money(150m, "USD"), row.CostAsMoney);
        Assert.Equal(new Money(200m, "USD"), row.NetValueAsMoney);
        Assert.Equal(new Money(50m, "USD"), row.PnlAsMoney);

        row.Currency = "JPY";
        Assert.Equal("JPY", row.MarketValueAsMoney.Currency);
    }

    [Fact]
    public void PortfolioRow_ApplyBaseValuation_ConvertsNativeUsdToBaseTwd()
    {
        var row = new PortfolioRowViewModel
        {
            Exchange = "NASDAQ",
            Currency = "USD",
            Quantity = 2m,
            BuyPrice = 100m,
            CurrentPrice = 125m,
        };
        row.Refresh();

        row.ApplyBaseValuation("TWD", new Dictionary<string, decimal> { ["USD"] = 32m });

        Assert.True(row.IsCrossCurrency);
        Assert.Equal(new Money(6_400m, "TWD"), row.CostBaseAsMoney);
        Assert.Equal(new Money(8_000m, "TWD"), row.MarketValueBaseAsMoney);
        Assert.Equal(new Money(1_600m, "TWD"), row.PnlBaseAsMoney);
    }

    [Fact]
    public void CurrencyConverter_FormatsMoneyInNativeCurrencyWithoutServiceConversion()
    {
        var converter = new CurrencyConverter();

        Assert.Equal("$1,234", converter.Convert(new Money(1234m, "USD"), typeof(string), "amount", CultureInfo.InvariantCulture));
        Assert.Equal("+$1,234", converter.Convert(new Money(1234m, "USD"), typeof(string), "signed", CultureInfo.InvariantCulture));
        Assert.Equal("≈ $1,234.56", converter.Convert(new Money(1234.56m, "USD"), typeof(string), "price-approx", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void PortfolioRow_UsStockDoesNotUseTaiwanSellFeeEstimation()
    {
        var row = new PortfolioRowViewModel
        {
            Exchange = "NASDAQ",
            AssetType = AssetType.Stock,
            Currency = "USD",
            Quantity = 10m,
            BuyPrice = 100m,
            CurrentPrice = 120m,
        };

        row.Refresh();

        Assert.Equal(0m, row.EstimatedSellFee);
        Assert.Equal(1200m, row.NetValue);
        Assert.Equal(200m, row.Pnl);
    }
}
