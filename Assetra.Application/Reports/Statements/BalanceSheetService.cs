using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Interfaces.MultiAsset;
using Assetra.Core.Interfaces.Reports;
using Assetra.Core.Models;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Reports;

namespace Assetra.Application.Reports.Statements;

/// <summary>
/// 資產負債表：以 trade journal 為單一真實來源（current balance），
/// 投資市值優先取 <see cref="IPortfolioSnapshotRepository"/> 該日 snapshot；無 snapshot 則退而僅以 cash + 負債計。
/// v0.23：加入不動產（RealEstate equity）與保險（InsurancePolicy cash value）資產行項。
/// v0.24：加入退休專戶（RetirementAccount balance）與實物資產（PhysicalAsset current value）資產行項。
/// </summary>
public sealed class BalanceSheetService : IBalanceSheetService
{
    private readonly IAssetRepository _assets;
    private readonly ITradeRepository _trades;
    private readonly IPortfolioSnapshotRepository? _snapshots;
    private readonly IMultiCurrencyValuationService? _fx;
    private readonly IAppSettingsService? _settings;
    private readonly IRealEstateRepository? _realEstate;
    private readonly IInsurancePolicyRepository? _insurancePolicies;
    private readonly IRetirementAccountRepository? _retirementAccounts;
    private readonly IPhysicalAssetRepository? _physicalAssets;

    public BalanceSheetService(
        IAssetRepository assets,
        ITradeRepository trades,
        IPortfolioSnapshotRepository? snapshots = null,
        IMultiCurrencyValuationService? fx = null,
        IAppSettingsService? settings = null,
        IRealEstateRepository? realEstate = null,
        IInsurancePolicyRepository? insurancePolicies = null,
        IRetirementAccountRepository? retirementAccounts = null,
        IPhysicalAssetRepository? physicalAssets = null)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(trades);
        _assets = assets;
        _trades = trades;
        _snapshots = snapshots;
        _fx = fx;
        _settings = settings;
        _realEstate = realEstate;
        _insurancePolicies = insurancePolicies;
        _retirementAccounts = retirementAccounts;
        _physicalAssets = physicalAssets;
    }

    public async Task<BalanceSheet> GenerateAsync(DateOnly asOf, CancellationToken ct = default)
    {
        var asOfDt = asOf.ToDateTime(TimeOnly.MaxValue);
        var allTrades = await _trades.GetAllAsync(ct).ConfigureAwait(false);
        var tradesUntil = allTrades.Where(t => t.TradeDate <= asOfDt).ToList();

        var cashAssets = await _assets.GetItemsByTypeAsync(FinancialType.Asset).ConfigureAwait(false);
        var liabilityAssets = await _assets.GetItemsByTypeAsync(FinancialType.Liability).ConfigureAwait(false);
        var baseCurrency = GetBaseCurrency();
        PortfolioDailySnapshot? portfolioSnapshot = null;
        if (_snapshots is not null)
            portfolioSnapshot = await _snapshots.GetSnapshotAsync(asOf, ct).ConfigureAwait(false);
        var portfolioCurrencies = portfolioSnapshot is null
            ? Enumerable.Empty<string>()
            : new[] { portfolioSnapshot.Currency };
        var realEstateProperties = _realEstate is null
            ? Array.Empty<RealEstate>()
            : await _realEstate.GetAllAsync(ct).ConfigureAwait(false);
        var insurancePolicies = _insurancePolicies is null
            ? Array.Empty<InsurancePolicy>()
            : await _insurancePolicies.GetAllAsync(ct).ConfigureAwait(false);
        var retirementAccounts = _retirementAccounts is null
            ? Array.Empty<RetirementAccount>()
            : await _retirementAccounts.GetAllAsync(ct).ConfigureAwait(false);
        var physicalAssets = _physicalAssets is null
            ? Array.Empty<PhysicalAsset>()
            : await _physicalAssets.GetAllAsync(ct).ConfigureAwait(false);

        // Pre-resolve FX factor (foreign 1 unit → base) once per distinct currency.
        // Avoids N+1 ConvertAsync calls when many accounts share a currency.
        var fxFactors = await ResolveFxFactorsAsync(
            cashAssets
                .Concat(liabilityAssets)
                .Where(a => a.IsActive)
                .Select(a => a.Currency)
                .Concat(portfolioCurrencies)
                .Concat(realEstateProperties
                    .Where(p => p.Status == RealEstateStatus.Active && p.PurchaseDate <= asOf)
                    .Select(p => p.Currency))
                .Concat(insurancePolicies
                    .Where(p => p.Status == InsurancePolicyStatus.Active)
                    .Select(p => p.Currency))
                .Concat(retirementAccounts
                    .Where(a => a.Status == RetirementAccountStatus.Active)
                    .Select(a => a.Currency))
                .Concat(physicalAssets
                    .Where(a => a.Status == PhysicalAssetStatus.Active)
                    .Select(a => a.Currency)),
            asOf, baseCurrency, ct).ConfigureAwait(false);

        var assetRows = new List<StatementRow>();
        foreach (var item in cashAssets.Where(a => a.IsActive))
        {
            var bal = ComputeCashBalance(item.Id, tradesUntil);
            if (bal == 0m) continue;
            assetRows.Add(new StatementRow(item.Name, ConvertWithCache(bal, item.Currency, baseCurrency, fxFactors), "Cash"));
        }

        // 投資市值 (snapshot 優先)
        if (portfolioSnapshot is not null && portfolioSnapshot.MarketValue != 0m)
        {
            assetRows.Add(new StatementRow(
                "Portfolio",
                ConvertWithCache(portfolioSnapshot.MarketValue, portfolioSnapshot.Currency, baseCurrency, fxFactors),
                "Investments"));
        }

        AppendRealEstateRows(assetRows, realEstateProperties, asOf, baseCurrency, fxFactors);
        AppendInsuranceRows(assetRows, insurancePolicies, baseCurrency, fxFactors);
        AppendRetirementRows(assetRows, retirementAccounts, baseCurrency, fxFactors);
        AppendPhysicalAssetRows(assetRows, physicalAssets, baseCurrency, fxFactors);

        var assetTotal = assetRows.Sum(r => r.Amount);

        var liabilityRows = new List<StatementRow>();
        foreach (var item in liabilityAssets.Where(a => a.IsActive))
        {
            var bal = ComputeLiabilityBalance(item, tradesUntil);
            if (bal == 0m) continue;
            liabilityRows.Add(new StatementRow(item.Name, ConvertWithCache(bal, item.Currency, baseCurrency, fxFactors), item.IsCreditCard ? "Credit Card" : "Loan"));
        }
        var liabilityTotal = liabilityRows.Sum(r => r.Amount);

        return new BalanceSheet(
            AsOf: asOf,
            Assets: new StatementSection("Assets", assetRows, assetTotal),
            Liabilities: new StatementSection("Liabilities", liabilityRows, liabilityTotal),
            NetWorth: assetTotal - liabilityTotal);
    }

    private void AppendRealEstateRows(
        List<StatementRow> rows,
        IReadOnlyList<RealEstate> properties,
        DateOnly asOf,
        string? baseCurrency,
        Dictionary<string, decimal?> fxFactors)
    {
        foreach (var prop in properties.Where(p => p.Status == RealEstateStatus.Active && p.PurchaseDate <= asOf))
        {
            var equity = prop.Equity;
            if (equity == 0m) continue;
            rows.Add(new StatementRow(
                prop.Name,
                ConvertWithCache(equity, prop.Currency, baseCurrency, fxFactors),
                "Real Estate"));
        }
    }

    private void AppendRetirementRows(
        List<StatementRow> rows,
        IReadOnlyList<RetirementAccount> accounts,
        string? baseCurrency,
        Dictionary<string, decimal?> fxFactors)
    {
        foreach (var account in accounts.Where(a => a.Status == RetirementAccountStatus.Active))
        {
            if (account.Balance == 0m) continue;
            rows.Add(new StatementRow(
                account.Name,
                ConvertWithCache(account.Balance, account.Currency, baseCurrency, fxFactors),
                "Retirement"));
        }
    }

    private void AppendPhysicalAssetRows(
        List<StatementRow> rows,
        IReadOnlyList<PhysicalAsset> assets,
        string? baseCurrency,
        Dictionary<string, decimal?> fxFactors)
    {
        foreach (var asset in assets.Where(a => a.Status == PhysicalAssetStatus.Active))
        {
            if (asset.CurrentValue == 0m) continue;
            rows.Add(new StatementRow(
                asset.Name,
                ConvertWithCache(asset.CurrentValue, asset.Currency, baseCurrency, fxFactors),
                "Physical Asset"));
        }
    }

    private void AppendInsuranceRows(
        List<StatementRow> rows,
        IReadOnlyList<InsurancePolicy> policies,
        string? baseCurrency,
        Dictionary<string, decimal?> fxFactors)
    {
        foreach (var policy in policies.Where(p => p.Status == InsurancePolicyStatus.Active))
        {
            if (policy.CurrentCashValue == 0m) continue;
            rows.Add(new StatementRow(
                policy.Name,
                ConvertWithCache(policy.CurrentCashValue, policy.Currency, baseCurrency, fxFactors),
                "Insurance"));
        }
    }

    /// <summary>
    /// 預先計算每個外幣（非 base / 非空）對 base currency 的 1 單位換算因子，避免 row-level N+1 ConvertAsync。
    /// 缺匯率以 null 標記，套用時 fallback 為原值。
    /// </summary>
    private async Task<Dictionary<string, decimal?>> ResolveFxFactorsAsync(
        IEnumerable<string> currencies, DateOnly asOf, string? baseCurrency, CancellationToken ct)
    {
        var result = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
        if (_fx is null || baseCurrency is null) return result;

        var distinct = currencies
            .Where(c => !string.IsNullOrWhiteSpace(c)
                        && !string.Equals(c, baseCurrency, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var ccy in distinct)
        {
            ct.ThrowIfCancellationRequested();
            result[ccy] = await _fx.ConvertAsync(1m, ccy, baseCurrency, asOf, ct).ConfigureAwait(false);
        }
        return result;
    }

    private decimal ConvertWithCache(decimal amount, string fromCcy, string? baseCurrency, Dictionary<string, decimal?> factors)
    {
        if (_fx is null || baseCurrency is null) return amount;
        if (string.IsNullOrWhiteSpace(fromCcy)) return amount;
        if (string.Equals(fromCcy, baseCurrency, StringComparison.OrdinalIgnoreCase)) return amount;
        return factors.TryGetValue(fromCcy, out var factor) && factor is { } f ? amount * f : amount;
    }

    private string? GetBaseCurrency()
    {
        var baseCurrency = _settings?.Current.BaseCurrency;
        return string.IsNullOrWhiteSpace(baseCurrency) ? null : baseCurrency;
    }

    private static decimal ComputeCashBalance(Guid accountId, IEnumerable<Trade> trades)
    {
        decimal bal = 0m;
        foreach (var t in trades)
        {
            if (t.CashAccountId == accountId)
                bal += PrimaryCashDelta(t);
            if (t.Type == TradeType.Transfer && t.ToCashAccountId == accountId)
                bal += t.CashAmount ?? 0m;
        }
        return bal;
    }

    private static decimal PrimaryCashDelta(Trade t) => t.Type switch
    {
        TradeType.Income or TradeType.Deposit or TradeType.CashDividend or TradeType.LoanBorrow
            => t.CashAmount ?? 0m,
        TradeType.Withdrawal or TradeType.CreditCardPayment
            => -(t.CashAmount ?? 0m),
        TradeType.Transfer => -(t.CashAmount ?? 0m),
        TradeType.Buy => -((t.Price * t.Quantity) + (t.Commission ?? 0m)),
        TradeType.Sell => (t.Price * t.Quantity) - (t.Commission ?? 0m),
        TradeType.LoanRepay => -((t.Principal ?? 0m) + (t.InterestPaid ?? 0m)),
        _ => 0m,
    };

    private static decimal ComputeLiabilityBalance(AssetItem item, IEnumerable<Trade> trades)
    {
        decimal bal = 0m;
        foreach (var t in trades)
        {
            // 信用卡：以 LiabilityAssetId 連結
            if (item.IsCreditCard && t.LiabilityAssetId == item.Id)
            {
                bal += t.Type switch
                {
                    TradeType.CreditCardCharge => t.CashAmount ?? 0m,
                    TradeType.CreditCardPayment => -(t.CashAmount ?? 0m),
                    _ => 0m,
                };
            }
            // 貸款：以 LoanLabel 對應 item.Name
            else if (item.IsLoan && string.Equals(t.LoanLabel, item.Name, StringComparison.OrdinalIgnoreCase))
            {
                bal += t.Type switch
                {
                    TradeType.LoanBorrow => t.CashAmount ?? 0m,
                    TradeType.LoanRepay => -(t.Principal ?? 0m),
                    _ => 0m,
                };
            }
        }
        return bal;
    }
}
