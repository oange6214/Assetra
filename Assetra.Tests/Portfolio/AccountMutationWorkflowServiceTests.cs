using Moq;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Xunit;

namespace Assetra.Tests.Portfolio;

public sealed class AccountMutationWorkflowServiceTests
{
    private static (AccountMutationWorkflowService, Mock<IAssetRepository>, Mock<ITradeRepository>) Build()
    {
        var assetRepo = new Mock<IAssetRepository>();
        var tradeRepo = new Mock<ITradeRepository>();
        var service = new AccountMutationWorkflowService(assetRepo.Object, tradeRepo.Object);
        return (service, assetRepo, tradeRepo);
    }

    [Fact]
    public async Task DeleteAsync_DeletesReferencingTradesBeforeAccount()
    {
        var (service, assetRepo, tradeRepo) = Build();
        var id = Guid.NewGuid();

        var sequence = new List<string>();
        tradeRepo.Setup(r => r.RemoveByAccountIdAsync(id, default))
                 .Callback(() => sequence.Add("trades"))
                 .Returns(Task.CompletedTask);
        assetRepo.Setup(r => r.DeleteItemAsync(id))
                 .Callback(() => sequence.Add("account"))
                 .Returns(Task.CompletedTask);

        var result = await service.DeleteAsync(id);

        Assert.True(result.Success);
        Assert.Equal(["trades", "account"], sequence);
    }

    [Fact]
    public async Task DeleteAsync_WithNoReferences_StillSucceeds()
    {
        var (service, assetRepo, tradeRepo) = Build();
        var id = Guid.NewGuid();

        tradeRepo.Setup(r => r.RemoveByAccountIdAsync(id, default)).Returns(Task.CompletedTask);
        assetRepo.Setup(r => r.DeleteItemAsync(id)).Returns(Task.CompletedTask);

        var result = await service.DeleteAsync(id);

        Assert.True(result.Success);
        tradeRepo.Verify(r => r.RemoveByAccountIdAsync(id, default), Times.Once);
        assetRepo.Verify(r => r.DeleteItemAsync(id), Times.Once);
    }
}
