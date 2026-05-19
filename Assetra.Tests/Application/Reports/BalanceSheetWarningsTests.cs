using Assetra.Application.Reports.Statements;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models;
using Moq;
using Xunit;

namespace Assetra.Tests.Application.Reports;

/// <summary>
/// Reports-Loading-UX U6 — verifies that <see cref="BalanceSheetService"/>
/// surfaces a user-facing warning when the FX provider can't convert a
/// currency that's actually present in held positions (vs silently
/// rendering the unconverted amount, which used to look like data
/// corruption).
/// </summary>
public sealed class BalanceSheetWarningsTests
{
    [Fact]
    public async Task GenerateAsync_WhenFxReturnsNullForActiveCurrency_AddsWarning()
    {
        var assetRepo = new Mock<IAssetRepository>();
        var tradeRepo = new Mock<ITradeRepository>();
        var fx = new Mock<IMultiCurrencyValuationService>();
        var settings = new Mock<IAppSettingsService>();

        // User holds a USD cash account, base currency is TWD, but FX
        // service can't resolve USD→TWD → expect a warning.
        var usdCash = new AssetItem(
            Id: Guid.NewGuid(),
            Name: "美金活存",
            Type: FinancialType.Asset,
            GroupId: null,
            Currency: "USD",
            CreatedDate: DateOnly.FromDateTime(DateTime.Today));

        assetRepo
            .Setup(r => r.GetItemsByTypeAsync(FinancialType.Asset))
            .ReturnsAsync(new[] { usdCash });
        assetRepo
            .Setup(r => r.GetItemsByTypeAsync(FinancialType.Liability))
            .ReturnsAsync(Array.Empty<AssetItem>());
        tradeRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Trade>());

        fx.Setup(f => f.ConvertAsync(1m, "USD", "TWD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((decimal?)null);

        settings.Setup(s => s.Current).Returns(new AppSettings { BaseCurrency = "TWD" });

        var service = new BalanceSheetService(
            assetRepo.Object, tradeRepo.Object,
            snapshots: null,
            fx: fx.Object,
            settings: settings.Object);

        var result = await service.GenerateAsync(DateOnly.FromDateTime(DateTime.Today));

        Assert.NotNull(result.Warnings);
        var warning = Assert.Single(result.Warnings!);
        Assert.Contains("USD", warning);
        Assert.Contains("TWD", warning);
    }

    [Fact]
    public async Task GenerateAsync_WhenAllSameCurrency_NoWarnings()
    {
        var assetRepo = new Mock<IAssetRepository>();
        var tradeRepo = new Mock<ITradeRepository>();
        var fx = new Mock<IMultiCurrencyValuationService>();
        var settings = new Mock<IAppSettingsService>();

        var twdCash = new AssetItem(
            Id: Guid.NewGuid(),
            Name: "台幣活存",
            Type: FinancialType.Asset,
            GroupId: null,
            Currency: "TWD",
            CreatedDate: DateOnly.FromDateTime(DateTime.Today));

        assetRepo
            .Setup(r => r.GetItemsByTypeAsync(FinancialType.Asset))
            .ReturnsAsync(new[] { twdCash });
        assetRepo
            .Setup(r => r.GetItemsByTypeAsync(FinancialType.Liability))
            .ReturnsAsync(Array.Empty<AssetItem>());
        tradeRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Trade>());
        settings.Setup(s => s.Current).Returns(new AppSettings { BaseCurrency = "TWD" });

        var service = new BalanceSheetService(
            assetRepo.Object, tradeRepo.Object,
            snapshots: null,
            fx: fx.Object,
            settings: settings.Object);

        var result = await service.GenerateAsync(DateOnly.FromDateTime(DateTime.Today));

        // Same-currency case never invokes ConvertAsync — no warnings.
        Assert.True(result.Warnings is null || result.Warnings.Count == 0);
    }
}
