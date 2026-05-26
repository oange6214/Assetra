using Moq;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
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
            .Callback<Guid, string, string, CancellationToken>((id, _, _, _) => updated.Add(id))
            .Returns(Task.CompletedTask);

        var service = new PositionMetadataWorkflowService(repo.Object);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };

        await service.UpdateAsync(new PositionMetadataUpdateRequest(ids, "TSMC", "USD"));

        Assert.Equal(ids, updated);
    }

    [Fact]
    public async Task UpdateGroupAsync_UpdatesEveryEntryWithoutChangingOtherMetadata()
    {
        var groupId = Guid.NewGuid();
        var first = new PortfolioEntry(
            Guid.NewGuid(),
            "2330",
            "TWSE",
            AssetType.Stock,
            "台積電",
            "TWD",
            IsActive: true,
            IsEtf: false,
            PortfolioGroupId: PortfolioGroup.DefaultId);
        var second = new PortfolioEntry(
            Guid.NewGuid(),
            "AAPL",
            "NASDAQ",
            AssetType.Stock,
            "Apple",
            "USD",
            IsActive: true,
            IsEtf: false,
            PortfolioGroupId: PortfolioGroup.DefaultId);

        var repo = new Mock<IPortfolioRepository>();
        repo.Setup(r => r.GetEntriesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { first, second });
        var updated = new List<PortfolioEntry>();
        repo.Setup(r => r.UpdateAsync(It.IsAny<PortfolioEntry>(), It.IsAny<CancellationToken>()))
            .Callback<PortfolioEntry, CancellationToken>((entry, _) => updated.Add(entry))
            .Returns(Task.CompletedTask);

        var service = new PositionMetadataWorkflowService(repo.Object);

        await service.UpdateGroupAsync(new PositionGroupUpdateRequest(
            new[] { first.Id, second.Id },
            groupId));

        Assert.Collection(
            updated,
            entry =>
            {
                Assert.Equal(first.Id, entry.Id);
                Assert.Equal("台積電", entry.DisplayName);
                Assert.Equal("TWD", entry.Currency);
                Assert.Equal(groupId, entry.PortfolioGroupId);
            },
            entry =>
            {
                Assert.Equal(second.Id, entry.Id);
                Assert.Equal("Apple", entry.DisplayName);
                Assert.Equal("USD", entry.Currency);
                Assert.Equal(groupId, entry.PortfolioGroupId);
            });
    }
}
