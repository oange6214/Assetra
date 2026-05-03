using Assetra.Application.MultiAsset;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;
using Xunit;

namespace Assetra.Tests.Application.MultiAsset;

public class InsuranceCashValueCalculatorTests
{
    private static InsurancePolicy MakePolicy(
        string name,
        decimal cashValue,
        decimal annualPremium = 50_000m,
        InsurancePolicyStatus status = InsurancePolicyStatus.Active) =>
        new(Guid.NewGuid(), name, "P001", InsuranceType.WholeLife, "國泰",
            new DateOnly(2020, 1, 1), null, 2_000_000m, cashValue, annualPremium,
            "TWD", status, null, new EntityVersion());

    private static InsurancePremiumRecord MakePremium(Guid policyId, decimal amount) =>
        new(Guid.NewGuid(), policyId, new DateOnly(2024, 1, 15), amount, "TWD", null);

    [Fact]
    public async Task GetTotalCashValue_ActiveOnly()
    {
        var active = MakePolicy("Active", 200_000m);
        var lapsed = MakePolicy("Lapsed", 50_000m, status: InsurancePolicyStatus.Lapsed);
        var calc = new InsuranceCashValueCalculator(
            new StubPolicyRepo([active, lapsed]),
            new StubPremiumRepo([]));

        var result = await calc.GetTotalCashValueAsync();

        Assert.Equal(200_000m, result);
    }

    [Fact]
    public async Task GetTotalAnnualPremium_SumsActiveOnly()
    {
        var p1 = MakePolicy("P1", 100_000m, annualPremium: 30_000m);
        var p2 = MakePolicy("P2", 80_000m, annualPremium: 20_000m);
        var lapsed = MakePolicy("Lapsed", 10_000m, annualPremium: 15_000m, status: InsurancePolicyStatus.Lapsed);
        var calc = new InsuranceCashValueCalculator(
            new StubPolicyRepo([p1, p2, lapsed]),
            new StubPremiumRepo([]));

        var result = await calc.GetTotalAnnualPremiumAsync();

        Assert.Equal(50_000m, result);
    }

    [Fact]
    public async Task GetCashValueSummaries_IncludesTotalPremiumsPaid()
    {
        var policy = MakePolicy("Life", 150_000m, annualPremium: 24_000m);
        var premiums = new List<InsurancePremiumRecord>
        {
            MakePremium(policy.Id, 24_000m),
            MakePremium(policy.Id, 24_000m),
        };
        var calc = new InsuranceCashValueCalculator(
            new StubPolicyRepo([policy]),
            new StubPremiumRepo(premiums));

        var summaries = await calc.GetCashValueSummariesAsync();

        Assert.Single(summaries);
        Assert.Equal(48_000m, summaries[0].TotalPremiumsPaid);
        Assert.Equal(150_000m, summaries[0].CashValue);
    }

    [Fact]
    public async Task GetCashValueSummaries_EmptyRepo_ReturnsEmpty()
    {
        var calc = new InsuranceCashValueCalculator(
            new StubPolicyRepo([]),
            new StubPremiumRepo([]));

        var summaries = await calc.GetCashValueSummariesAsync();

        Assert.Empty(summaries);
    }

    [Fact]
    public async Task Totals_WithFxAndBaseCurrency_ConvertBeforeSumming()
    {
        var twd = MakePolicy("TWD", 100_000m, annualPremium: 10_000m);
        var usd = MakePolicy("USD", 1_000m, annualPremium: 100m) with { Currency = "USD" };
        var calc = new InsuranceCashValueCalculator(
            new StubPolicyRepo([twd, usd]),
            new StubPremiumRepo([new InsurancePremiumRecord(Guid.NewGuid(), usd.Id, new DateOnly(2024, 1, 15), 50m, "USD", null)]),
            new StubFx(("USD", "TWD"), 32m),
            new StubSettings(new AppSettings(BaseCurrency: "TWD")));

        var totalCashValue = await calc.GetTotalCashValueAsync();
        var totalAnnualPremium = await calc.GetTotalAnnualPremiumAsync();
        var summaries = await calc.GetCashValueSummariesAsync();

        Assert.Equal(132_000m, totalCashValue);
        Assert.Equal(13_200m, totalAnnualPremium);
        var usdSummary = Assert.Single(summaries.Where(s => s.Policy.Id == usd.Id));
        Assert.Equal(32_000m, usdSummary.CashValue);
        Assert.Equal(1_600m, usdSummary.TotalPremiumsPaid);
        Assert.Equal("TWD", usdSummary.Currency);
        Assert.Equal(3_200m, usdSummary.AnnualPremium);
    }

    // ── Stubs ──

    private sealed class StubPolicyRepo(IReadOnlyList<InsurancePolicy> data) : IInsurancePolicyRepository
    {
        public Task<IReadOnlyList<InsurancePolicy>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(data);
        public Task<InsurancePolicy?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(data.FirstOrDefault(x => x.Id == id));
        public Task AddAsync(InsurancePolicy policy, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateAsync(InsurancePolicy policy, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubPremiumRepo(IReadOnlyList<InsurancePremiumRecord> data) : IInsurancePremiumRecordRepository
    {
        public Task<IReadOnlyList<InsurancePremiumRecord>> GetByPolicyAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<InsurancePremiumRecord>>(data.Where(r => r.PolicyId == id).ToList());
        public Task<IReadOnlyList<InsurancePremiumRecord>> GetByPeriodAsync(DateOnly from, DateOnly to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<InsurancePremiumRecord>>(data.Where(r => r.PaidDate >= from && r.PaidDate <= to).ToList());
        public Task AddAsync(InsurancePremiumRecord record, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubFx((string From, string To) pair, decimal rate) : IMultiCurrencyValuationService
    {
        public Task<decimal?> ConvertAsync(decimal amount, string from, string to, DateOnly asOf, CancellationToken ct = default)
            => Task.FromResult<decimal?>(
                string.Equals(from, pair.From, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(to, pair.To, StringComparison.OrdinalIgnoreCase)
                    ? amount * rate
                    : amount);
    }

    private sealed class StubSettings(AppSettings current) : IAppSettingsService
    {
        public AppSettings Current { get; } = current;
        public event Action? Changed
        {
            add { }
            remove { }
        }
        public Task SaveAsync(AppSettings settings) => Task.CompletedTask;
    }
}
