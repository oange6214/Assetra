using Assetra.Application.Portfolio.Dtos;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Moq;
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

    [Fact]
    public async Task RecordAsync_WhenScheduleInsertFails_RollsBackTradesAndAsset()
    {
        var assetRepo = new Mock<IAssetRepository>();
        var loanRepo = new Mock<ILoanScheduleRepository>();
        var txService = new Mock<ITransactionService>();
        AssetItem? addedAsset = null;
        var recordedTrades = new List<Trade>();
        var deletedTrades = new List<Trade>();

        assetRepo.Setup(r => r.AddItemAsync(It.IsAny<AssetItem>()))
            .Callback<AssetItem>(asset => addedAsset = asset)
            .Returns(Task.CompletedTask);
        assetRepo.Setup(r => r.DeleteItemAsync(It.IsAny<Guid>()))
            .Returns(Task.CompletedTask);
        loanRepo.Setup(r => r.BulkInsertAsync(It.IsAny<IEnumerable<LoanScheduleEntry>>()))
            .ThrowsAsync(new InvalidOperationException("schedule fail"));
        loanRepo.Setup(r => r.DeleteByAssetAsync(It.IsAny<Guid>()))
            .Returns(Task.CompletedTask);
        txService.Setup(r => r.RecordAsync(It.IsAny<Trade>()))
            .Callback<Trade>(recordedTrades.Add)
            .Returns(Task.CompletedTask);
        txService.Setup(r => r.DeleteAsync(It.IsAny<Trade>()))
            .Callback<Trade>(deletedTrades.Add)
            .Returns(Task.CompletedTask);

        var service = new LoanMutationWorkflowService(
            assetRepo.Object,
            loanRepo.Object,
            txService.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordAsync(new LoanTransactionRequest(
            TradeType.LoanBorrow,
            120000m,
            DateTime.UtcNow,
            "房貸",
            Guid.NewGuid(),
            null,
            600m,
            AmortAnnualRate: 0.02m,
            AmortTermMonths: 12,
            FirstPaymentDate: new DateOnly(2026, 5, 1))));

        Assert.NotNull(addedAsset);
        Assert.Equal(2, recordedTrades.Count);
        Assert.Equal(recordedTrades.Select(t => t.Id).Reverse(), deletedTrades.Select(t => t.Id));
        loanRepo.Verify(r => r.DeleteByAssetAsync(addedAsset!.Id), Times.Once);
        assetRepo.Verify(r => r.DeleteItemAsync(addedAsset!.Id), Times.Once);
    }

    [Fact]
    public async Task RecordAsync_LoanRepayWithScheduleEntryId_MarksSchedulePaidWithNewTrade()
    {
        var assetRepo = new Mock<IAssetRepository>();
        var loanRepo = new Mock<ILoanScheduleRepository>();
        var txService = new Mock<ITransactionService>();
        Trade? recordedTrade = null;
        var scheduleEntryId = Guid.NewGuid();
        var tradeDate = new DateTime(2026, 5, 30, 8, 0, 0, DateTimeKind.Utc);

        txService.Setup(r => r.RecordAsync(It.IsAny<Trade>()))
            .Callback<Trade>(trade => recordedTrade = trade)
            .Returns(Task.CompletedTask);

        var service = new LoanMutationWorkflowService(
            assetRepo.Object,
            loanRepo.Object,
            txService.Object);

        await service.RecordAsync(new LoanTransactionRequest(
            TradeType.LoanRepay,
            25_978m,
            tradeDate,
            "台新 7y A",
            Guid.NewGuid(),
            null,
            0m,
            Principal: 22_833m,
            InterestPaid: 3_145m,
            LoanScheduleEntryId: scheduleEntryId));

        Assert.NotNull(recordedTrade);
        loanRepo.Verify(r => r.MarkPaidAsync(scheduleEntryId, tradeDate, recordedTrade!.Id), Times.Once);
    }
}
