using Moq;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Xunit;

namespace Assetra.Tests.Portfolio;

public sealed class AccountMutationWorkflowServiceTests
{
    [Fact]
    public async Task DeleteAsync_WhenReferenced_ReturnsBlockedAndSkipsDelete()
    {
        var repo = new Mock<IAssetRepository>();
        repo.Setup(r => r.HasTradeReferencesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var service = new AccountMutationWorkflowService(repo.Object);
        var result = await service.DeleteAsync(Guid.NewGuid());

        Assert.False(result.Success);
        Assert.Equal(3, result.ReferenceCount);
        repo.Verify(r => r.DeleteItemAsync(It.IsAny<Guid>()), Times.Never);
    }
}
