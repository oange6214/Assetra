using Moq;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Portfolio;

public sealed class SellWorkflowServiceTests
{
    [Fact]
    public async Task RecordAsync_FullSell_WritesTrade_ArchivesEntries_AndLogsZeroQuantity()
    {
        var tradeRepo = new Mock<ITradeRepository>();
        var portfolioRepo = new Mock<IPortfolioRepository>();
        var logRepo = new Mock<IPortfolioPositionLogRepository>();
        var positionQuery = new Mock<IPositionQueryService>();
        positionQuery.Setup(s => s.ComputeRealizedPnlAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), 650m, 1000m, 1200m))
            .ReturnsAsync(50_000m);

        Trade? savedTrade = null;
        PortfolioPositionLog? savedLog = null;
        var archivedIds = new List<Guid>();
        tradeRepo.Setup(r => r.AddAsync(It.IsAny<Trade>()))
            .Callback<Trade, CancellationToken>((t, _) => savedTrade = t)
            .Returns(Task.CompletedTask);
        logRepo.Setup(r => r.LogAsync(It.IsAny<PortfolioPositionLog>(), It.IsAny<CancellationToken>()))
            .Callback<PortfolioPositionLog, CancellationToken>((l, _) => savedLog = l)
            .Returns(Task.CompletedTask);
        portfolioRepo.Setup(r => r.ArchiveAsync(It.IsAny<Guid>()))
            .Callback<Guid, CancellationToken>((id, _) => archivedIds.Add(id))
            .Returns(Task.CompletedTask);

        var service = new SellWorkflowService(
            tradeRepo.Object,
            portfolioRepo.Object,
            logRepo.Object,
            positionQuery.Object);
        var entryIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var result = await service.RecordAsync(new SellWorkflowRequest(
            PortfolioEntryId: Guid.NewGuid(),
            Symbol: "2330",
            Exchange: "TWSE",
            Name: "TSMC",
            BuyPrice: 600m,
            CurrentQuantity: 1000,
            SellQuantity: 1000,
            SellPrice: 650m,
            Commission: 1200m,
            CommissionDiscount: 0.6m,
            CashAccountId: Guid.NewGuid(),
            EntryIdsToArchive: entryIds));

        Assert.NotNull(savedTrade);
        Assert.Equal(TradeType.Sell, savedTrade!.Type);
        Assert.Equal(50_000m, savedTrade.RealizedPnl);
        Assert.Equal(8.3333333333333333333333333300m, savedTrade.RealizedPnlPct);
        Assert.Equal(entryIds, archivedIds);
        Assert.NotNull(savedLog);
        Assert.Equal(0, savedLog!.Quantity);
        Assert.Equal(0, result.RemainingQuantity);
    }
}
