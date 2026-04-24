using Moq;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Portfolio;

public sealed class CreditCardMutationWorkflowServiceTests
{
    [Fact]
    public async Task CreateAsync_PersistsCreditCardMetadata()
    {
        var repo = new Mock<IAssetRepository>();
        AssetItem? saved = null;
        repo.Setup(r => r.AddItemAsync(It.IsAny<AssetItem>()))
            .Callback<AssetItem>(item => saved = item)
            .Returns(Task.CompletedTask);

        var service = new CreditCardMutationWorkflowService(repo.Object);
        var result = await service.CreateAsync(new CreateCreditCardRequest(
            "  Cube Card  ",
            "TWD",
            new DateOnly(2026, 4, 23),
            8,
            23,
            200000m,
            " 國泰世華 "));

        Assert.NotNull(saved);
        Assert.Equal(FinancialType.Liability, saved!.Type);
        Assert.Equal(LiabilitySubtype.CreditCard, saved.LiabilitySubtype);
        Assert.Equal(8, saved.BillingDay);
        Assert.Equal(23, saved.DueDay);
        Assert.Equal(200000m, saved.CreditLimit);
        Assert.Equal("國泰世華", saved.IssuerName);
        Assert.Equal(saved, result.CreditCard);
    }

    [Fact]
    public async Task UpdateAsync_RewritesCreditCardMetadata()
    {
        var repo = new Mock<IAssetRepository>();
        AssetItem? saved = null;
        repo.Setup(r => r.UpdateItemAsync(It.IsAny<AssetItem>()))
            .Callback<AssetItem>(item => saved = item)
            .Returns(Task.CompletedTask);

        var service = new CreditCardMutationWorkflowService(repo.Object);
        var cardId = Guid.NewGuid();

        var result = await service.UpdateAsync(new UpdateCreditCardRequest(
            cardId,
            " FlyGo ",
            "USD",
            new DateOnly(2026, 4, 1),
            5,
            20,
            5000m,
            null));

        Assert.NotNull(saved);
        Assert.Equal(cardId, saved!.Id);
        Assert.Equal("FlyGo", saved.Name);
        Assert.Equal("USD", saved.Currency);
        Assert.Equal(LiabilitySubtype.CreditCard, saved.LiabilitySubtype);
        Assert.Equal(5, saved.BillingDay);
        Assert.Equal(20, saved.DueDay);
        Assert.Equal(5000m, saved.CreditLimit);
        Assert.Null(saved.IssuerName);
        Assert.Equal(saved, result.CreditCard);
    }
}
