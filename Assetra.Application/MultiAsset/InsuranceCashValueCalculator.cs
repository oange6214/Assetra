using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models.MultiAsset;

namespace Assetra.Application.MultiAsset;

/// <summary>
/// 保險現金價值計算器：彙整所有有效保單的現金價值與已繳保費。
/// </summary>
public sealed class InsuranceCashValueCalculator : IInsuranceCashValueCalculator
{
    private readonly IInsurancePolicyRepository _policies;
    private readonly IInsurancePremiumRecordRepository _premiums;
    private readonly IMultiCurrencyValuationService? _fx;
    private readonly IAppSettingsService? _settings;

    public InsuranceCashValueCalculator(
        IInsurancePolicyRepository policies,
        IInsurancePremiumRecordRepository premiums,
        IMultiCurrencyValuationService? fx = null,
        IAppSettingsService? settings = null)
    {
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(premiums);
        _policies = policies;
        _premiums = premiums;
        _fx = fx;
        _settings = settings;
    }

    public async Task<decimal> GetTotalCashValueAsync(CancellationToken ct = default)
    {
        var all = await _policies.GetAllAsync(ct).ConfigureAwait(false);
        var total = 0m;
        var asOf = DateOnly.FromDateTime(DateTime.Today);
        foreach (var policy in all.Where(p => p.Status == InsurancePolicyStatus.Active))
            total += await ConvertToBaseOrOriginalAsync(policy.CurrentCashValue, policy.Currency, asOf, ct).ConfigureAwait(false);
        return total;
    }

    public async Task<decimal> GetTotalAnnualPremiumAsync(CancellationToken ct = default)
    {
        var all = await _policies.GetAllAsync(ct).ConfigureAwait(false);
        var total = 0m;
        var asOf = DateOnly.FromDateTime(DateTime.Today);
        foreach (var policy in all.Where(p => p.Status == InsurancePolicyStatus.Active))
            total += await ConvertToBaseOrOriginalAsync(policy.AnnualPremium, policy.Currency, asOf, ct).ConfigureAwait(false);
        return total;
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
            var cashValue = await ConvertToBaseOrOriginalAsync(
                policy.CurrentCashValue, policy.Currency, DateOnly.FromDateTime(DateTime.Today), ct).ConfigureAwait(false);
            var annualPremium = await ConvertToBaseOrOriginalAsync(
                policy.AnnualPremium, policy.Currency, DateOnly.FromDateTime(DateTime.Today), ct).ConfigureAwait(false);
            var totalPaid = 0m;
            foreach (var premium in premiums)
                totalPaid += await ConvertToBaseOrOriginalAsync(
                    premium.Amount, premium.Currency, premium.PaidDate, ct).ConfigureAwait(false);
            results.Add(new InsuranceCashValueSummary(
                policy, cashValue, totalPaid, GetBaseCurrency() ?? policy.Currency, annualPremium));
        }
        return results;
    }

    private string? GetBaseCurrency() => _settings?.Current.BaseCurrency;

    private async Task<decimal> ConvertToBaseOrOriginalAsync(
        decimal amount,
        string fromCurrency,
        DateOnly asOf,
        CancellationToken ct)
    {
        var baseCurrency = GetBaseCurrency();
        if (_fx is null
            || string.IsNullOrWhiteSpace(baseCurrency)
            || string.IsNullOrWhiteSpace(fromCurrency)
            || string.Equals(fromCurrency, baseCurrency, StringComparison.OrdinalIgnoreCase))
            return amount;

        var converted = await _fx.ConvertAsync(amount, fromCurrency, baseCurrency, asOf, ct).ConfigureAwait(false);
        return converted ?? amount;
    }
}
