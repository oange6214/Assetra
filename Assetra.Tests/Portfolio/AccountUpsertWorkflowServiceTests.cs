using Moq;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Application.Portfolio.Services;
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
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync(() => saved);

        var service = new AccountUpsertWorkflowService(repo.Object);
        var result = await service.CreateAsync(new CreateAccountRequest("  Rich Bank  ", "USD", new DateOnly(2026, 4, 21)));

        Assert.NotNull(saved);
        Assert.Equal("Rich Bank", saved!.Name);
        Assert.Equal(FinancialType.Asset, saved.Type);
        Assert.Equal("USD", result.Account.Currency);
    }

    [Fact]
    public async Task UpdateAsync_PersistsSubtypeAndReturnsReadBackAccount()
    {
        var id = Guid.NewGuid();
        var repo = new Mock<IAssetRepository>();
        var existing = new AssetItem(
            id,
            "富邦",
            FinancialType.Asset,
            null,
            "TWD",
            new DateOnly(2026, 4, 20),
            IsActive: false);
        AssetItem? saved = null;

        repo.SetupSequence(r => r.GetByIdAsync(id))
            .ReturnsAsync(existing)
            .ReturnsAsync(() => saved);
        repo.Setup(r => r.UpdateItemAsync(It.IsAny<AssetItem>()))
            .Callback<AssetItem>(item => saved = item)
            .Returns(Task.CompletedTask);

        var service = new AccountUpsertWorkflowService(repo.Object);
        var result = await service.UpdateAsync(new UpdateAccountRequest(
            id,
            "  富邦銀行  ",
            "TWD",
            new DateOnly(2026, 4, 20),
            Subtype: "銀行活存"));

        Assert.NotNull(saved);
        Assert.Equal("富邦銀行", saved!.Name);
        Assert.Equal("銀行活存", saved.Subtype);
        Assert.Equal(new Guid("11111111-1111-1111-1111-111111111101"), saved.GroupId);
        Assert.False(saved.IsActive);
        Assert.Same(saved, result.Account);
    }
}
