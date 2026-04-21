using Moq;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Dtos;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Xunit;

namespace Assetra.Tests.Portfolio;

public sealed class LoanMutationWorkflowServiceTests
{
    [Fact]
    public async Task RecordAsync_PersistsPlanArtifactsAndTrades()
    {
        var workflow = new Mock<ITransactionWorkflowService>();
        var assetRepo = new Mock<IAssetRepository>();
        var loanRepo = new Mock<ILoanScheduleRepository>();
        var txService = new Mock<ITransactionService>();

        var liability = new AssetItem(Guid.NewGuid(), "房貸", FinancialType.Liability, null, "TWD", new DateOnly(2026, 4, 21));
        var schedule = new[]
        {
            new LoanScheduleEntry(Guid.NewGuid(), liability.Id, 1, new DateOnly(2026, 5, 1), 1100m, 1000m, 100m, 9000m, false, null, null)
        };
        var trade = new Trade(Guid.NewGuid(), "", "", "房貸", TradeType.LoanBorrow, DateTime.UtcNow, 0m, 1, null, null, 10000m);
        workflow.Setup(w => w.CreateLoanPlan(It.IsAny<LoanTransactionRequest>()))
            .Returns(new TransactionWorkflowPlan([trade], liability, schedule));

        var service = new LoanMutationWorkflowService(
            workflow.Object,
            assetRepo.Object,
            loanRepo.Object,
            txService.Object);

        var result = await service.RecordAsync(new LoanTransactionRequest(
            TradeType.LoanBorrow,
            10000m,
            DateTime.UtcNow,
            "房貸",
            Guid.NewGuid(),
            "note",
            0m));

        Assert.Same(liability, result.LiabilityAsset);
        assetRepo.Verify(r => r.AddItemAsync(liability), Times.Once);
        loanRepo.Verify(r => r.BulkInsertAsync(schedule), Times.Once);
        txService.Verify(r => r.RecordAsync(trade), Times.Once);
    }
}
