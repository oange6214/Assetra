using Moq;
using Assetra.AppLayer.Portfolio.Dtos;
using Assetra.AppLayer.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Portfolio;

public sealed class TradeDeletionWorkflowServiceTests
{
    [Fact]
    public async Task DeleteAsync_BlockedByNegativeQuantity_ReturnsBlockedAndSkipsDeletes()
    {
        var tradeRepo = new Mock<ITradeRepository>();
        var portfolioRepo = new Mock<IPortfolioRepository>();
        var positionQuery = new Mock<IPositionQueryService>();
        var entryId = Guid.NewGuid();
        positionQuery.Setup(q => q.GetPositionAsync(entryId))
            .ReturnsAsync(new PositionSnapshot(entryId, 50m, 0m, 0m, 0m, null));

        var service = new TradeDeletionWorkflowService(
            tradeRepo.Object,
            portfolioRepo.Object,
            positionQuery.Object);

        var result = await service.DeleteAsync(new TradeDeletionRequest(
            Guid.NewGuid(),
            TradeType.Buy,
            "2330",
            100,
            entryId));

        Assert.False(result.Success);
        Assert.True(result.BlockedBySell);
        tradeRepo.Verify(r => r.RemoveAsync(It.IsAny<Guid>()), Times.Never);
        portfolioRepo.Verify(r => r.RemoveAsync(It.IsAny<Guid>()), Times.Never);
    }
}
