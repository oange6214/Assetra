using Moq;
using Assetra.AppLayer.Portfolio.Dtos;
using Assetra.AppLayer.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Portfolio;

public sealed class TradeMetadataWorkflowServiceTests
{
    [Fact]
    public async Task UpdateAsync_WhenTradeExists_UpdatesDateAndNoteOnly()
    {
        var tradeId = Guid.NewGuid();
        var original = new Trade(
            Id: tradeId,
            Symbol: "2330",
            Exchange: "TWSE",
            Name: "TSMC",
            Type: TradeType.Sell,
            TradeDate: new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            Price: 650m,
            Quantity: 1000,
            RealizedPnl: 50_000m,
            RealizedPnlPct: 8.3m,
            Note: "old");

        var tradeRepo = new Mock<ITradeRepository>();
        tradeRepo.Setup(r => r.GetAllAsync()).ReturnsAsync([original]);

        Trade? updatedTrade = null;
        tradeRepo.Setup(r => r.UpdateAsync(It.IsAny<Trade>()))
            .Callback<Trade>(t => updatedTrade = t)
            .Returns(Task.CompletedTask);

        var service = new TradeMetadataWorkflowService(tradeRepo.Object);
        var changed = await service.UpdateAsync(new TradeMetadataUpdateRequest(
            tradeId,
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            "new"));

        Assert.True(changed);
        Assert.NotNull(updatedTrade);
        Assert.Equal("new", updatedTrade!.Note);
        Assert.Equal(new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc), updatedTrade.TradeDate);
        Assert.Equal(650m, updatedTrade.Price);
        Assert.Equal(1000, updatedTrade.Quantity);
        Assert.Equal(50_000m, updatedTrade.RealizedPnl);
    }
}
