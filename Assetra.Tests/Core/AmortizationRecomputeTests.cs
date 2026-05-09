using Assetra.Core.Models;
using Assetra.Core.Services;
using Xunit;

namespace Assetra.Tests.Core;

/// <summary>
/// Tests for <see cref="AmortizationService.RecomputeUnpaidTail"/> — the
/// "preserve paid, regenerate unpaid" path used by the EditLiability flow.
/// </summary>
public class AmortizationRecomputeTests
{
    private static readonly Guid AssetId = Guid.NewGuid();
    private static readonly DateOnly Start = new(2025, 1, 1);

    private static IReadOnlyList<LoanScheduleEntry> SeedSchedule(int paidCount, int totalCount)
    {
        var entries = AmortizationService.Generate(AssetId, 1_200_000m, 0.024m, totalCount, Start);
        var output = new List<LoanScheduleEntry>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            output.Add(i < paidCount
                ? entries[i] with { IsPaid = true, PaidAt = DateTime.UtcNow.AddDays(-i), TradeId = Guid.NewGuid() }
                : entries[i]);
        }
        return output;
    }

    [Fact]
    public void Recompute_KeepsAllPaidEntriesVerbatim()
    {
        var existing = SeedSchedule(paidCount: 6, totalCount: 24);

        var combined = AmortizationService.RecomputeUnpaidTail(
            AssetId, originalPrincipal: 1_200_000m,
            newAnnualRate: 0.030m, newTermMonths: 30,
            originalFirstPaymentDate: Start,
            existingEntries: existing);

        for (var i = 0; i < 6; i++)
        {
            Assert.True(combined[i].IsPaid);
            Assert.Equal(existing[i].Id, combined[i].Id);
            Assert.Equal(existing[i].TradeId, combined[i].TradeId);
            Assert.Equal(existing[i].PrincipalAmount, combined[i].PrincipalAmount);
            Assert.Equal(existing[i].InterestAmount, combined[i].InterestAmount);
        }
    }

    [Fact]
    public void Recompute_RegeneratedTail_HasCorrectPeriodNumbersAndCount()
    {
        var existing = SeedSchedule(paidCount: 10, totalCount: 24);

        var combined = AmortizationService.RecomputeUnpaidTail(
            AssetId, originalPrincipal: 1_200_000m,
            newAnnualRate: 0.030m, newTermMonths: 36,
            originalFirstPaymentDate: Start,
            existingEntries: existing);

        Assert.Equal(36, combined.Count);
        Assert.Equal(26, combined.Count(e => !e.IsPaid));

        // Tail period numbers are sequential starting at paidCount + 1.
        for (var i = 10; i < combined.Count; i++)
        {
            Assert.Equal(i + 1, combined[i].Period);
            Assert.False(combined[i].IsPaid);
        }
    }

    [Fact]
    public void Recompute_RemainingPrincipal_EqualsOriginalMinusPaidPrincipal()
    {
        var existing = SeedSchedule(paidCount: 8, totalCount: 24);
        var paidPrincipal = existing.Where(e => e.IsPaid).Sum(e => e.PrincipalAmount);
        var expectedRemaining = 1_200_000m - paidPrincipal;

        var combined = AmortizationService.RecomputeUnpaidTail(
            AssetId, originalPrincipal: 1_200_000m,
            newAnnualRate: 0.025m, newTermMonths: 30,
            originalFirstPaymentDate: Start,
            existingEntries: existing);

        // Sum of unpaid principal should equal remaining (within 1 NTD rounding).
        var newUnpaidPrincipal = combined.Where(e => !e.IsPaid).Sum(e => e.PrincipalAmount);
        Assert.InRange(newUnpaidPrincipal - expectedRemaining, -1m, 1m);
    }

    [Fact]
    public void Recompute_FirstUnpaidDueDate_IsNextMonthAfterLastPaid()
    {
        var existing = SeedSchedule(paidCount: 5, totalCount: 24);
        var lastPaidDue = existing[4].DueDate;

        var combined = AmortizationService.RecomputeUnpaidTail(
            AssetId, originalPrincipal: 1_200_000m,
            newAnnualRate: 0.025m, newTermMonths: 30,
            originalFirstPaymentDate: Start,
            existingEntries: existing);

        Assert.Equal(lastPaidDue.AddMonths(1), combined[5].DueDate);
    }

    [Fact]
    public void Recompute_NoPaidYet_StartsFromOriginalFirstPaymentDate()
    {
        var existing = SeedSchedule(paidCount: 0, totalCount: 12);

        var combined = AmortizationService.RecomputeUnpaidTail(
            AssetId, originalPrincipal: 1_200_000m,
            newAnnualRate: 0.030m, newTermMonths: 18,
            originalFirstPaymentDate: Start,
            existingEntries: existing);

        Assert.Equal(Start, combined[0].DueDate);
        Assert.Equal(18, combined.Count);
    }

    [Fact]
    public void Recompute_NewTermLessThanPaidCount_Throws()
    {
        var existing = SeedSchedule(paidCount: 10, totalCount: 24);

        Assert.Throws<InvalidOperationException>(() => AmortizationService.RecomputeUnpaidTail(
            AssetId, originalPrincipal: 1_200_000m,
            newAnnualRate: 0.025m, newTermMonths: 8,
            originalFirstPaymentDate: Start,
            existingEntries: existing));
    }

    [Fact]
    public void Recompute_NoRemainingPrincipal_Throws()
    {
        // All paid → remainingPrincipal = 0
        var existing = SeedSchedule(paidCount: 24, totalCount: 24);

        Assert.Throws<InvalidOperationException>(() => AmortizationService.RecomputeUnpaidTail(
            AssetId, originalPrincipal: 1_200_000m,
            newAnnualRate: 0.025m, newTermMonths: 30,
            originalFirstPaymentDate: Start,
            existingEntries: existing));
    }

    [Fact]
    public void Recompute_NegativeRate_Throws()
    {
        var existing = SeedSchedule(paidCount: 0, totalCount: 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => AmortizationService.RecomputeUnpaidTail(
            AssetId, 1_000_000m, -0.01m, 12, Start, existing));
    }
}
