using Moq;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Portfolio;

public sealed class CreditCardTransactionWorkflowServiceTests
{
    [Fact]
    public async Task ChargeAsync_CreatesCreditCardChargeTrade()
    {
        var cardId = Guid.NewGuid();
        var assetRepo = new Mock<IAssetRepository>();
        assetRepo.Setup(r => r.GetItemsAsync())
            .ReturnsAsync([
                new AssetItem(
                    Id: cardId,
                    Name: "Cube",
                    Type: FinancialType.Liability,
                    GroupId: null,
                    Currency: "TWD",
                    CreatedDate: new DateOnly(2026, 4, 23),
                    LiabilitySubtype: LiabilitySubtype.CreditCard)
            ]);

        var tx = new Mock<ITransactionService>();
        Trade? recorded = null;
        tx.Setup(t => t.RecordAsync(It.IsAny<Trade>()))
            .Callback<Trade>(trade => recorded = trade)
            .Returns(Task.CompletedTask);

        var service = new CreditCardTransactionWorkflowService(assetRepo.Object, tx.Object);
        var result = await service.ChargeAsync(new CreditCardChargeRequest(
            cardId,
            "Cube",
            new DateTime(2026, 4, 23, 10, 0, 0, DateTimeKind.Local),
            1234m,
            " Uber Eats "));

        Assert.NotNull(recorded);
        Assert.Equal(TradeType.CreditCardCharge, recorded!.Type);
        Assert.Equal(cardId, recorded.LiabilityAssetId);
        Assert.Equal(1234m, recorded.CashAmount);
        Assert.Equal("Uber Eats", recorded.Note);
        Assert.Equal(recorded, result.Trade);
    }

    [Fact]
    public async Task PayAsync_CreatesCreditCardPaymentTrade()
    {
        var cardId = Guid.NewGuid();
        var cashAccountId = Guid.NewGuid();
        var assetRepo = new Mock<IAssetRepository>();
        assetRepo.Setup(r => r.GetItemsAsync())
            .ReturnsAsync([
                new AssetItem(
                    Id: cardId,
                    Name: "FlyGo",
                    Type: FinancialType.Liability,
                    GroupId: null,
                    Currency: "TWD",
                    CreatedDate: new DateOnly(2026, 4, 23),
                    LiabilitySubtype: LiabilitySubtype.CreditCard)
            ]);

        var tx = new Mock<ITransactionService>();
        Trade? recorded = null;
        tx.Setup(t => t.RecordAsync(It.IsAny<Trade>()))
            .Callback<Trade>(trade => recorded = trade)
            .Returns(Task.CompletedTask);

        var service = new CreditCardTransactionWorkflowService(assetRepo.Object, tx.Object);
        var result = await service.PayAsync(new CreditCardPaymentRequest(
            cardId,
            "FlyGo",
            cashAccountId,
            "台幣主帳戶",
            new DateTime(2026, 4, 24, 9, 0, 0, DateTimeKind.Local),
            5000m,
            null));

        Assert.NotNull(recorded);
        Assert.Equal(TradeType.CreditCardPayment, recorded!.Type);
        Assert.Equal(cardId, recorded.LiabilityAssetId);
        Assert.Equal(cashAccountId, recorded.CashAccountId);
        Assert.Equal(5000m, recorded.CashAmount);
        Assert.Equal("繳款自 台幣主帳戶", recorded.Note);
        Assert.Equal(recorded, result.Trade);
    }

    [Fact]
    public async Task ChargeAsync_WhenCardMissing_Throws()
    {
        var assetRepo = new Mock<IAssetRepository>();
        assetRepo.Setup(r => r.GetItemsAsync()).ReturnsAsync([]);
        var tx = new Mock<ITransactionService>();

        var service = new CreditCardTransactionWorkflowService(assetRepo.Object, tx.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChargeAsync(
            new CreditCardChargeRequest(Guid.NewGuid(), "Missing", DateTime.Today, 10m, null)));
    }
}
