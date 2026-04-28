using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;

namespace Assetra.Application.MultiAsset;

/// <summary>
/// 保險現金價值計算器：彙整所有有效保單的現金價值與已繳保費。
/// </summary>
public sealed class InsuranceCashValueCalculator : IInsuranceCashValueCalculator
{
    private readonly IInsurancePolicyRepository _policies;
    private readonly IInsurancePremiumRecordRepository _premiums;

    public InsuranceCashValueCalculator(
        IInsurancePolicyRepository policies,
        IInsurancePremiumRecordRepository premiums)
    {
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(premiums);
        _policies = policies;
        _premiums = premiums;
    }

    public async Task<decimal> GetTotalCashValueAsync(CancellationToken ct = default)
    {
        var all = await _policies.GetAllAsync(ct).ConfigureAwait(false);
        return all.Where(p => p.Status == InsurancePolicyStatus.Active)
                  .Sum(p => p.CurrentCashValue);
    }

    public async Task<decimal> GetTotalAnnualPremiumAsync(CancellationToken ct = default)
    {
        var all = await _policies.GetAllAsync(ct).ConfigureAwait(false);
        return all.Where(p => p.Status == InsurancePolicyStatus.Active)
                  .Sum(p => p.AnnualPremium);
    }

    public async Task<IReadOnlyList<InsuranceCashValueSummary>> GetCashValueSummariesAsync(
        CancellationToken ct = default)
    {
        var all = await _policies.GetAllAsync(ct).ConfigureAwait(false);
        var active = all.Where(p => p.Status == InsurancePolicyStatus.Active).ToList();

        var results = new List<InsuranceCashValueSummary>(active.Count);
        foreach (var policy in active)
        {
            ct.ThrowIfCancellationRequested();
            var premiums = await _premiums.GetByPolicyAsync(policy.Id, ct).ConfigureAwait(false);
            var totalPaid = premiums.Sum(r => r.Amount);
            results.Add(new InsuranceCashValueSummary(policy, policy.CurrentCashValue, totalPaid));
        }
        return results;
    }
}
