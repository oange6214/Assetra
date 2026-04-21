using Moq;
using Assetra.AppLayer.Portfolio.Dtos;
using Assetra.AppLayer.Portfolio.Services;
using Assetra.Core.Interfaces;
using Xunit;

namespace Assetra.Tests.Portfolio;

public sealed class PositionMetadataWorkflowServiceTests
{
    [Fact]
    public async Task UpdateAsync_UpdatesEveryEntryId()
    {
        var repo = new Mock<IPortfolioRepository>();
        var updated = new List<Guid>();
        repo.Setup(r => r.UpdateMetadataAsync(It.IsAny<Guid>(), "TSMC", "USD"))
            .Callback<Guid, string, string>((id, _, _) => updated.Add(id))
            .Returns(Task.CompletedTask);

        var service = new PositionMetadataWorkflowService(repo.Object);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };

        await service.UpdateAsync(new PositionMetadataUpdateRequest(ids, "TSMC", "USD"));

        Assert.Equal(ids, updated);
    }
}
