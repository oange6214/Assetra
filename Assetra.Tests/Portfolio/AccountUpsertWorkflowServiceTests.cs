using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Moq;
using Xunit;

namespace Assetra.Tests.Portfolio;

public sealed class AccountUpsertWorkflowServiceTests
{
    [Fact]
    public async Task CreateAsync_TrimsNameAndPersistsAccount()
    {
        var repo = new Mock<IAssetRepository>();
        AssetItem? saved = null;
        repo.Setup(r => r.CreateOrReviveAccountAsync(It.IsAny<AssetItem>(), It.IsAny<CancellationToken>()))
            .Callback<AssetItem, CancellationToken>((item, _) => saved = item)
            .ReturnsAsync((AssetItem item, CancellationToken _) =>
                new AccountCreateOutcome(item.Id, AccountCreateStatus.Created));
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
    public async Task CreateAsync_WhenRepoRevivesTombstone_ReturnsRevivedAccount()
    {
        // 復活情境：repo 回報 Revived + 既有列 Id；workflow 應回讀並回傳該啟用帳戶（蝦皮 fix）。
        var existingId = Guid.NewGuid();
        var repo = new Mock<IAssetRepository>();
        repo.Setup(r => r.CreateOrReviveAccountAsync(It.IsAny<AssetItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountCreateOutcome(existingId, AccountCreateStatus.Revived));
        var revived = new AssetItem(
            existingId, "蝦皮", FinancialType.Asset, null, "TWD",
            new DateOnly(2026, 5, 30), IsActive: true, Subtype: "電子支付");
        repo.Setup(r => r.GetByIdAsync(existingId)).ReturnsAsync(revived);

        var service = new AccountUpsertWorkflowService(repo.Object);
        var result = await service.CreateAsync(
            new CreateAccountRequest("蝦皮", "TWD", new DateOnly(2026, 5, 30), Subtype: "電子支付"));

        Assert.Equal(existingId, result.Account.Id);
        Assert.True(result.Account.IsActive);
    }

    [Fact]
    public async Task CreateAsync_WhenActiveDuplicate_ThrowsDuplicateAccountException()
    {
        // 仍啟用中的同名同幣別帳戶 → 丟領域例外，由 UI 轉成友善提示（而非 SQLite 例外 crash）。
        var repo = new Mock<IAssetRepository>();
        repo.Setup(r => r.CreateOrReviveAccountAsync(It.IsAny<AssetItem>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccountCreateOutcome(Guid.NewGuid(), AccountCreateStatus.DuplicateActive));

        var service = new AccountUpsertWorkflowService(repo.Object);

        await Assert.ThrowsAsync<DuplicateAccountException>(() =>
            service.CreateAsync(new CreateAccountRequest("富邦", "TWD", new DateOnly(2026, 5, 30))));
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
