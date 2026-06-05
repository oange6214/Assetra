using Assetra.Application.Loans.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Moq;
using Xunit;

namespace Assetra.Tests.Application.Loans;

public sealed class LoanScheduleServiceTests
{
    [Fact]
    public async Task GetScheduleByAssetAsync_ReconcilesDeletedLinkedTradesBeforeReturningRows()
    {
        var calls = new List<string>();
        var assetId = Guid.NewGuid();
        var scheduleRepo = new Mock<ILoanScheduleRepository>();
        scheduleRepo.Setup(r => r.ClearPaidWithoutActiveTradeAsync(assetId))
            .Callback(() => calls.Add("clear"))
            .Returns(Task.CompletedTask);
        scheduleRepo.Setup(r => r.ReconcilePaidFromActiveRepaymentsAsync(assetId))
            .Callback(() => calls.Add("project"))
            .Returns(Task.CompletedTask);
        scheduleRepo.Setup(r => r.GetByAssetAsync(assetId))
            .Callback(() => calls.Add("read"))
            .ReturnsAsync(Array.Empty<LoanScheduleEntry>());
        var service = new LoanScheduleService(scheduleRepo.Object);

        await service.GetScheduleByAssetAsync(assetId);

        Assert.Equal(new[] { "clear", "project", "read" }, calls);
    }
}
