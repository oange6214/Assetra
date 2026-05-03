using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Models.MultiAsset;

namespace Assetra.Application.MultiAsset;

/// <summary>
/// 退休專戶預測服務：以複利模型推算未來餘額，並彙整活躍帳戶摘要。
/// </summary>
public sealed class RetirementProjectionService : IRetirementProjectionService
{
    private readonly IRetirementAccountRepository _accounts;
    private readonly IRetirementContributionRepository _contributions;
    private readonly IMultiCurrencyValuationService? _fx;
    private readonly IAppSettingsService? _settings;

    public RetirementProjectionService(
        IRetirementAccountRepository accounts,
        IRetirementContributionRepository contributions,
        IMultiCurrencyValuationService? fx = null,
        IAppSettingsService? settings = null)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(contributions);
        _accounts = accounts;
        _contributions = contributions;
        _fx = fx;
        _settings = settings;
    }

    public async Task<decimal> GetTotalBalanceAsync(CancellationToken ct = default)
    {
        var all = await _accounts.GetAllAsync(ct).ConfigureAwait(false);
        var total = 0m;
        var asOf = DateOnly.FromDateTime(DateTime.Today);
        foreach (var account in all.Where(a => a.Status == RetirementAccountStatus.Active))
            total += await ConvertToBaseOrOriginalAsync(account.Balance, account.Currency, asOf, ct).ConfigureAwait(false);
        return total;
    }

    public async Task<RetirementProjection?> ProjectAsync(
        Guid accountId,
        int currentAge,
        decimal annualReturnRate,
        decimal annualContribution,
        CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(accountId, ct).ConfigureAwait(false);
        if (account is null)
            return null;

        var years = Math.Max(0, account.LegalWithdrawalAge - currentAge);
        var rate = annualReturnRate;
        var balance = account.Balance;

        decimal projected;
        decimal totalContributions = annualContribution * years;

        if (rate == 0m)
        {
            projected = balance + totalContributions;
        }
        else
        {
            // FV = PV * (1+r)^n + PMT * ((1+r)^n - 1) / r
            var growthFactor = (decimal)Math.Pow((double)(1m + rate), years);
            projected = balance * growthFactor + annualContribution * (growthFactor - 1m) / rate;
        }

        return new RetirementProjection(
            AccountId: account.Id,
            CurrentBalance: balance,
            YearsToWithdrawal: years,
            ProjectedBalance: projected,
            TotalContributions: totalContributions);
    }

    public async Task<IReadOnlyList<RetirementAccountSummary>> GetAccountSummariesAsync(
        CancellationToken ct = default)
    {
        var all = await _accounts.GetAllAsync(ct).ConfigureAwait(false);
        var active = all.Where(a => a.Status == RetirementAccountStatus.Active).ToList();

        var results = new List<RetirementAccountSummary>(active.Count);
        foreach (var account in active)
        {
            ct.ThrowIfCancellationRequested();
            var contribs = await _contributions.GetByAccountAsync(account.Id, ct).ConfigureAwait(false);
            var latest = contribs.OrderByDescending(c => c.Year).FirstOrDefault();
            var latestTotal = latest?.TotalAmount ?? 0m;
            results.Add(new RetirementAccountSummary(account, latestTotal));
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
        {
            return amount;
        }

        var converted = await _fx.ConvertAsync(amount, fromCurrency, baseCurrency, asOf, ct).ConfigureAwait(false);
        return converted ?? amount;
    }
}
