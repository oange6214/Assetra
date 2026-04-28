using Moq;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Portfolio;

public sealed class AddAssetWorkflowServiceTests
{
    [Fact]
    public async Task CreateManualAssetAsync_PersistsEntry_AndReturnsSnapshot()
    {
        var search = new Mock<IStockSearchService>();
        var portfolioRepo = new Mock<IPortfolioRepository>();
        var logRepo = new Mock<IPortfolioPositionLogRepository>();
        var txService = new Mock<ITransactionService>();

        PortfolioEntry? addedEntry = null;
        portfolioRepo.Setup(r => r.AddAsync(It.IsAny<PortfolioEntry>()))
            .Callback<PortfolioEntry, CancellationToken>((e, _) => addedEntry = e)
            .Returns(Task.CompletedTask);

        var service = new AddAssetWorkflowService(
            search.Object,
            portfolioRepository: portfolioRepo.Object,
            positionLogRepository: logRepo.Object,
            transactionService: txService.Object);

        var result = await service.CreateManualAssetAsync(new ManualAssetCreateRequest(
            Symbol: "BTC",
            Exchange: string.Empty,
            Name: "BTC",
            AssetType: AssetType.Crypto,
            Quantity: 0.5m,
            TotalCost: 1_000_000m,
            UnitPrice: 2_000_000m,
            AcquiredOn: new DateOnly(2026, 4, 21)));

        Assert.NotNull(addedEntry);
        Assert.Equal(AssetType.Crypto, addedEntry!.AssetType);
        Assert.Equal("BTC", addedEntry.Symbol);
        Assert.Equal(addedEntry.Id, result.Snapshot.PortfolioEntryId);
        Assert.Equal(0.5m, result.Snapshot.Quantity);
        Assert.Equal(1_000_000m, result.Snapshot.TotalCost);
        Assert.Equal(2_000_000m, result.Snapshot.AverageCost);
    }
}
