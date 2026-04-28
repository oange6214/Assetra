using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Interfaces.Reports;
using Assetra.Core.Models;
using Assetra.Core.Models.Reports;

namespace Assetra.Application.Reports.Statements;

/// <summary>
/// 資產負債表：以 trade journal 為單一真實來源（current balance），
/// 投資市值優先取 <see cref="IPortfolioSnapshotRepository"/> 該日 snapshot；無 snapshot 則退而僅以 cash + 負債計。
/// </summary>
public sealed class BalanceSheetService : IBalanceSheetService
{
    private readonly IAssetRepository _assets;
    private readonly ITradeRepository _trades;
    private readonly IPortfolioSnapshotRepository? _snapshots;
    private readonly IMultiCurrencyValuationService? _fx;
    private readonly string? _baseCurrency;

    public BalanceSheetService(
        IAssetRepository assets,
        ITradeRepository trades,
        IPortfolioSnapshotRepository? snapshots = null,
        IMultiCurrencyValuationService? fx = null,
        string? baseCurrency = null)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(trades);
        _assets = assets;
        _trades = trades;
        _snapshots = snapshots;
        _fx = fx;
        _baseCurrency = string.IsNullOrWhiteSpace(baseCurrency) ? null : baseCurrency;
    }

    public async Task<BalanceSheet> GenerateAsync(DateOnly asOf, CancellationToken ct = default)
    {
        var asOfDt = asOf.ToDateTime(TimeOnly.MaxValue);
        var allTrades = await _trades.GetAllAsync(ct).ConfigureAwait(false);
        var tradesUntil = allTrades.Where(t => t.TradeDate <= asOfDt).ToList();

        var cashAssets = await _assets.GetItemsByTypeAsync(FinancialType.Asset).ConfigureAwait(false);
        var liabilityAssets = await _assets.GetItemsByTypeAsync(FinancialType.Liability).ConfigureAwait(false);

        // Pre-resolve FX factor (foreign 1 unit → base) once per distinct currency.
        // Avoids N+1 ConvertAsync calls when many accounts share a currency.
        var fxFactors = await ResolveFxFactorsAsync(
            cashAssets.Concat(liabilityAssets).Where(a => a.IsActive).Select(a => a.Currency),
            asOf, ct).ConfigureAwait(false);

        var assetRows = new List<StatementRow>();
        foreach (var item in cashAssets.Where(a => a.IsActive))
        {
            var bal = ComputeCashBalance(item.Id, tradesUntil);
            if (bal == 0m) continue;
            assetRows.Add(new StatementRow(item.Name, ConvertWithCache(bal, item.Currency, fxFactors), "Cash"));
        }

        // 投資市值 (snapshot 優先)
        if (_snapshots is not null)
        {
            var snap = await _snapshots.GetSnapshotAsync(asOf, ct).ConfigureAwait(false);
            if (snap is not null && snap.MarketValue != 0m)
                assetRows.Add(new StatementRow("Portfolio", snap.MarketValue, "Investments"));
        }

        var assetTotal = assetRows.Sum(r => r.Amount);

        var liabilityRows = new List<StatementRow>();
        foreach (var item in liabilityAssets.Where(a => a.IsActive))
        {
            var bal = ComputeLiabilityBalance(item, tradesUntil);
            if (bal == 0m) continue;
            liabilityRows.Add(new StatementRow(item.Name, ConvertWithCache(bal, item.Currency, fxFactors), item.IsCreditCard ? "Credit Card" : "Loan"));
        }
        var liabilityTotal = liabilityRows.Sum(r => r.Amount);

        return new BalanceSheet(
            AsOf: asOf,
            Assets: new StatementSection("Assets", assetRows, assetTotal),
            Liabilities: new StatementSection("Liabilities", liabilityRows, liabilityTotal),
            NetWorth: assetTotal - liabilityTotal);
    }

    /// <summary>
    /// 預先計算每個外幣（非 base / 非空）對 base currency 的 1 單位換算因子，避免 row-level N+1 ConvertAsync。
    /// 缺匯率以 null 標記，套用時 fallback 為原值。
    /// </summary>
    private async Task<Dictionary<string, decimal?>> ResolveFxFactorsAsync(
        IEnumerable<string> currencies, DateOnly asOf, CancellationToken ct)
    {
        var result = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
        if (_fx is null || _baseCurrency is null) return result;

        var distinct = currencies
            .Where(c => !string.IsNullOrWhiteSpace(c)
                        && !string.Equals(c, _baseCurrency, StringComparison.OrdinalIgnoreCase))
            .Select(c => c.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var ccy in distinct)
        {
            ct.ThrowIfCancellationRequested();
            result[ccy] = await _fx.ConvertAsync(1m, ccy, _baseCurrency, asOf, ct).ConfigureAwait(false);
        }
        return result;
    }

    private decimal ConvertWithCache(decimal amount, string fromCcy, Dictionary<string, decimal?> factors)
    {
        if (_fx is null || _baseCurrency is null) return amount;
        if (string.IsNullOrWhiteSpace(fromCcy)) return amount;
        if (string.Equals(fromCcy, _baseCurrency, StringComparison.OrdinalIgnoreCase)) return amount;
        return factors.TryGetValue(fromCcy, out var factor) && factor is { } f ? amount * f : amount;
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
