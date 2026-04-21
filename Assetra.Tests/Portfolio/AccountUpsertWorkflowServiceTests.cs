using Moq;
using Assetra.AppLayer.Portfolio.Dtos;
using Assetra.AppLayer.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Portfolio;

public sealed class AccountUpsertWorkflowServiceTests
{
    [Fact]
    public async Task CreateAsync_TrimsNameAndPersistsAccount()
    {
        var repo = new Mock<IAssetRepository>();
        AssetItem? saved = null;
        repo.Setup(r => r.AddItemAsync(It.IsAny<AssetItem>()))
            .Callback<AssetItem>(item => saved = item)
            .Returns(Task.CompletedTask);

        var service = new AccountUpsertWorkflowService(repo.Object);
        var result = await service.CreateAsync(new CreateAccountRequest("  Rich Bank  ", "USD", new DateOnly(2026, 4, 21)));

        Assert.NotNull(saved);
        Assert.Equal("Rich Bank", saved!.Name);
        Assert.Equal(FinancialType.Asset, saved.Type);
        Assert.Equal("USD", result.Account.Currency);
    }
}
