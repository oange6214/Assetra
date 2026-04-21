using Assetra.Core.Services;
using Xunit;

namespace Assetra.Tests.Core;

public class AmortizationServiceTests
{
    private static readonly Guid AssetId = Guid.NewGuid();
    private static readonly DateOnly Start = new(2025, 2, 1);

    // ── Basic shape ──────────────────────────────────────────────────────────

    [Fact]
    public void Generate_ProducesCorrectPeriodCount()
    {
        var result = AmortizationService.Generate(AssetId, 1_000_000m, 0.026m, 84, Start);
        Assert.Equal(84, result.Count);
    }

    [Fact]
    public void Generate_PeriodsAreOneBasedAndSequential()
    {
        var result = AmortizationService.Generate(AssetId, 1_000_000m, 0.026m, 84, Start);
        for (var i = 0; i < result.Count; i++)
            Assert.Equal(i + 1, result[i].Period);
    }

    [Fact]
    public void Generate_DueDatesAreMonthlyFromFirstPaymentDate()
    {
        var result = AmortizationService.Generate(AssetId, 1_000_000m, 0.026m, 84, Start);
        for (var i = 0; i < result.Count; i++)
            Assert.Equal(Start.AddMonths(i), result[i].DueDate);
    }

    // ── Financial correctness ────────────────────────────────────────────────

    [Fact]
    public void Generate_LastPeriodRemainingIsZero()
    {
        var result = AmortizationService.Generate(AssetId, 1_000_000m, 0.026m, 84, Start);
        Assert.Equal(0m, result[^1].Remaining);
    }

    [Fact]
    public void Generate_SumOfPrincipalEqualsOriginalPrincipal()
    {
        var result = AmortizationService.Generate(AssetId, 1_000_000m, 0.026m, 84, Start);
        var totalPrincipal = result.Sum(e => e.PrincipalAmount);
        Assert.Equal(1_000_000m, totalPrincipal);
    }

    [Fact]
    public void Generate_TotalAmountEqualsPrincipalPlusInterestEachPeriod()
    {
        var result = AmortizationService.Generate(AssetId, 1_000_000m, 0.026m, 84, Start);
        foreach (var e in result)
            Assert.Equal(e.PrincipalAmount + e.InterestAmount, e.TotalAmount);
    }

    [Fact]
    public void Generate_RemainingDecreasesEachPeriod()
    {
        var result = AmortizationService.Generate(AssetId, 1_000_000m, 0.026m, 84, Start);
        for (var i = 1; i < result.Count; i++)
            Assert.True(result[i].Remaining < result[i - 1].Remaining,
                $"Period {i + 1} remaining should be less than period {i}");
    }

    [Fact]
    public void Generate_InterestDecreasesOverTime()
    {
        // Interest should shrink as the balance decreases
        var result = AmortizationService.Generate(AssetId, 1_000_000m, 0.026m, 84, Start);
        Assert.True(result[^1].InterestAmount < result[0].InterestAmount);
    }

    [Fact]
    public void Generate_MonthlyPaymentCloseToTheoreticalValue()
    {
        // Theoretical: NT$13,034 for 1M @ 2.6% / 84 months
        var result = AmortizationService.Generate(AssetId, 1_000_000m, 0.026m, 84, Start);
        var typical = result[0].TotalAmount; // first period (non-last)
        Assert.InRange(typical, 13_020m, 13_050m);
    }

    // ── Zero interest edge case ──────────────────────────────────────────────

    [Fact]
    public void Generate_ZeroInterestRate_EqualPrincipalPayments()
    {
        var result = AmortizationService.Generate(AssetId, 120_000m, 0m, 12, Start);
        Assert.Equal(12, result.Count);
        Assert.All(result, e => Assert.Equal(0m, e.InterestAmount));
        Assert.Equal(0m, result[^1].Remaining);
        Assert.Equal(120_000m, result.Sum(e => e.PrincipalAmount));
    }

    // ── All entries have correct AssetId ────────────────────────────────────

    [Fact]
    public void Generate_AllEntriesHaveCorrectAssetId()
    {
        var result = AmortizationService.Generate(AssetId, 500_000m, 0.026m, 24, Start);
        Assert.All(result, e => Assert.Equal(AssetId, e.AssetId));
    }

    // ── All entries start as unpaid ──────────────────────────────────────────

    [Fact]
    public void Generate_AllEntriesStartAsUnpaid()
    {
        var result = AmortizationService.Generate(AssetId, 500_000m, 0.026m, 24, Start);
        Assert.All(result, e =>
        {
            Assert.False(e.IsPaid);
            Assert.Null(e.PaidAt);
            Assert.Null(e.TradeId);
        });
    }

    // ── Guard conditions ─────────────────────────────────────────────────────

    [Fact]
    public void Generate_NegativePrincipal_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => AmortizationService.Generate(AssetId, -1m, 0.026m, 12, Start));

    [Fact]
    public void Generate_ZeroTermMonths_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => AmortizationService.Generate(AssetId, 1_000_000m, 0.026m, 0, Start));
}
