using Assetra.Application.Loans.Contracts;
using Assetra.Application.Portfolio.Contracts;
using Assetra.Application.Portfolio.Services;
using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Moq;
using Xunit;

namespace Assetra.Tests.Portfolio;

/// <summary>
/// Tests for the new <see cref="LiabilityMutationWorkflowService.UpdateAsync"/>
/// path (B 方案). Delete path remains exercised by integration scenarios.
/// </summary>
public sealed class LiabilityMutationWorkflowServiceTests
{
    private static AssetItem MakeLoanAsset(decimal rate = 0.025m, int term = 24) => new(
        Id:               Guid.NewGuid(),
        Name:             "國泰信貸",
        Type:             FinancialType.Liability,
        GroupId:          null,
        Currency:         "TWD",
        CreatedDate:      new DateOnly(2025, 1, 1),
        IsActive:         true,
        UpdatedAt:        null,
        LoanAnnualRate:   rate,
        LoanTermMonths:   term,
        LoanStartDate:    new DateOnly(2025, 1, 1),
        LoanHandlingFee:  null,
        LiabilitySubtype: LiabilitySubtype.Loan);

    private static AssetItem MakeCreditCardAsset(decimal? limit = null) => new(
        Id:               Guid.NewGuid(),
        Name:             "永豐 J Card",
        Type:             FinancialType.Liability,
        GroupId:          null,
        Currency:         "TWD",
        CreatedDate:      new DateOnly(2025, 1, 1),
        IsActive:         true,
        UpdatedAt:        null,
        LiabilitySubtype: LiabilitySubtype.CreditCard,
        BillingDay:       15,
        DueDay:           5,
        CreditLimit:      limit,
        IssuerName:       "永豐銀行");

    [Fact]
    public async Task UpdateAsync_NameChange_PersistsAndDoesNotTriggerRecompute()
    {
        var asset = MakeLoanAsset();
        var assetRepo = new Mock<IAssetRepository>();
        assetRepo.Setup(r => r.GetByIdAsync(asset.Id)).ReturnsAsync(asset);
        var tradeRepo = new Mock<ITradeRepository>();
        var recompute = new Mock<ILoanScheduleRecomputeService>();

        var sut = new LiabilityMutationWorkflowService(assetRepo.Object, tradeRepo.Object, recompute.Object);

        var result = await sut.UpdateAsync(new LiabilityUpdateRequest(
            AssetId: asset.Id,
            NewName: "國泰首購信貸"));

        Assert.True(result.Success);
        Assert.False(result.ScheduleRecomputed);
        assetRepo.Verify(r => r.UpdateItemAsync(
            It.Is<AssetItem>(a => a.Name == "國泰首購信貸")), Times.Once);
        recompute.Verify(r => r.RecomputeAsync(It.IsAny<LoanScheduleRecomputeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_LoanRateChange_WithRecomputeOptIn_TriggersRecompute()
    {
        var asset = MakeLoanAsset(rate: 0.025m, term: 24);
        var assetRepo = new Mock<IAssetRepository>();
        assetRepo.Setup(r => r.GetByIdAsync(asset.Id)).ReturnsAsync(asset);
        var tradeRepo = new Mock<ITradeRepository>();
        var recompute = new Mock<ILoanScheduleRecomputeService>();
        recompute.Setup(r => r.RecomputeAsync(It.IsAny<LoanScheduleRecomputeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LoanScheduleRecomputeResult(PreservedPaidCount: 6, RegeneratedUnpaidCount: 18, RemainingPrincipal: 600_000m));

        var sut = new LiabilityMutationWorkflowService(assetRepo.Object, tradeRepo.Object, recompute.Object);

        var result = await sut.UpdateAsync(new LiabilityUpdateRequest(
            AssetId:          asset.Id,
            NewAnnualRate:    0.030m,
            NewTermMonths:    24,
            RecomputeSchedule: true,
            OriginalPrincipal: 1_200_000m));

        Assert.True(result.Success);
        Assert.True(result.ScheduleRecomputed);
        Assert.Equal(6, result.PreservedPaidCount);
        Assert.Equal(18, result.RegeneratedUnpaidCount);
        recompute.Verify(r => r.RecomputeAsync(
            It.Is<LoanScheduleRecomputeRequest>(req =>
                req.AssetId == asset.Id &&
                req.NewAnnualRate == 0.030m &&
                req.OriginalPrincipal == 1_200_000m),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_RecomputeOptOut_DoesNotCallRecompute()
    {
        var asset = MakeLoanAsset();
        var assetRepo = new Mock<IAssetRepository>();
        assetRepo.Setup(r => r.GetByIdAsync(asset.Id)).ReturnsAsync(asset);
        var tradeRepo = new Mock<ITradeRepository>();
        var recompute = new Mock<ILoanScheduleRecomputeService>();

        var sut = new LiabilityMutationWorkflowService(assetRepo.Object, tradeRepo.Object, recompute.Object);

        var result = await sut.UpdateAsync(new LiabilityUpdateRequest(
            AssetId:          asset.Id,
            NewAnnualRate:    0.030m,
            NewTermMonths:    24,
            RecomputeSchedule: false,
            OriginalPrincipal: 1_200_000m));

        Assert.True(result.Success);
        Assert.False(result.ScheduleRecomputed);
        recompute.Verify(r => r.RecomputeAsync(It.IsAny<LoanScheduleRecomputeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_RecomputeWithoutOriginalPrincipal_Skips()
    {
        var asset = MakeLoanAsset();
        var assetRepo = new Mock<IAssetRepository>();
        assetRepo.Setup(r => r.GetByIdAsync(asset.Id)).ReturnsAsync(asset);
        var tradeRepo = new Mock<ITradeRepository>();
        var recompute = new Mock<ILoanScheduleRecomputeService>();

        var sut = new LiabilityMutationWorkflowService(assetRepo.Object, tradeRepo.Object, recompute.Object);

        var result = await sut.UpdateAsync(new LiabilityUpdateRequest(
            AssetId:          asset.Id,
            NewAnnualRate:    0.030m,
            NewTermMonths:    24,
            RecomputeSchedule: true));   // OriginalPrincipal omitted

        Assert.True(result.Success);
        Assert.False(result.ScheduleRecomputed);
    }

    [Fact]
    public async Task UpdateAsync_CreditCardLimitChange_PersistsWithoutRecompute()
    {
        var asset = MakeCreditCardAsset(limit: 100_000m);
        var assetRepo = new Mock<IAssetRepository>();
        assetRepo.Setup(r => r.GetByIdAsync(asset.Id)).ReturnsAsync(asset);
        var tradeRepo = new Mock<ITradeRepository>();
        var recompute = new Mock<ILoanScheduleRecomputeService>();

        var sut = new LiabilityMutationWorkflowService(assetRepo.Object, tradeRepo.Object, recompute.Object);

        var result = await sut.UpdateAsync(new LiabilityUpdateRequest(
            AssetId:        asset.Id,
            NewCreditLimit: 200_000m,
            NewBillingDay:  20,
            NewDueDay:      10));

        Assert.True(result.Success);
        Assert.False(result.ScheduleRecomputed);
        assetRepo.Verify(r => r.UpdateItemAsync(
            It.Is<AssetItem>(a =>
                a.CreditLimit == 200_000m &&
                a.BillingDay == 20 &&
                a.DueDay == 10)), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_AssetNotFound_Throws()
    {
        var assetRepo = new Mock<IAssetRepository>();
        assetRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>())).ReturnsAsync((AssetItem?)null);
        var tradeRepo = new Mock<ITradeRepository>();

        var sut = new LiabilityMutationWorkflowService(assetRepo.Object, tradeRepo.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.UpdateAsync(new LiabilityUpdateRequest(AssetId: Guid.NewGuid(), NewName: "X")));
    }

    [Fact]
    public async Task UpdateAsync_NonLiabilityAsset_Throws()
    {
        var asset = MakeLoanAsset() with { Type = FinancialType.Asset };
        var assetRepo = new Mock<IAssetRepository>();
        assetRepo.Setup(r => r.GetByIdAsync(asset.Id)).ReturnsAsync(asset);
        var tradeRepo = new Mock<ITradeRepository>();

        var sut = new LiabilityMutationWorkflowService(assetRepo.Object, tradeRepo.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.UpdateAsync(new LiabilityUpdateRequest(AssetId: asset.Id, NewName: "X")));
    }
}
