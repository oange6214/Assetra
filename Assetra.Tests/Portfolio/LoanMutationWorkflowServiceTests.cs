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
        var assetRepo = new Mock<IAssetRepository>();
        var loanRepo = new Mock<ILoanScheduleRepository>();
        var txService = new Mock<ITransactionService>();

        var service = new LoanMutationWorkflowService(
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

        txService.Verify(r => r.RecordAsync(
            It.Is<Trade>(t => t.Type == TradeType.LoanBorrow && t.CashAmount == 10000m)),
            Times.Once);
        Assert.Null(result.LiabilityAssetId);
        Assert.Null(result.ScheduleEntries);
    }

    [Fact]
    public async Task RecordAsync_WithAmortization_CreatesAssetAndSchedule()
    {
        var assetRepo = new Mock<IAssetRepository>();
        var loanRepo = new Mock<ILoanScheduleRepository>();
        var txService = new Mock<ITransactionService>();

        var service = new LoanMutationWorkflowService(
            assetRepo.Object,
            loanRepo.Object,
            txService.Object);

        var result = await service.RecordAsync(new LoanTransactionRequest(
            TradeType.LoanBorrow,
            120000m,
            DateTime.UtcNow,
            "房貸",
            Guid.NewGuid(),
            null,
            0m,
            AmortAnnualRate: 0.02m,
            AmortTermMonths: 12,
            FirstPaymentDate: new DateOnly(2026, 5, 1)));

        assetRepo.Verify(r => r.AddItemAsync(It.IsAny<AssetItem>()), Times.Once);
        loanRepo.Verify(r => r.BulkInsertAsync(It.IsAny<IReadOnlyList<LoanScheduleEntry>>()), Times.Once);
        Assert.NotNull(result.LiabilityAssetId);
        Assert.NotNull(result.ScheduleEntries);
        Assert.True(result.ScheduleEntries!.Count > 0);
    }
}
