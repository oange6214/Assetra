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
        var allTrades = await _trades.GetAllAsync().ConfigureAwait(false);
        var tradesUntil = allTrades.Where(t => t.TradeDate <= asOfDt).ToList();

        var cashAssets = await _assets.GetItemsByTypeAsync(FinancialType.Asset).ConfigureAwait(false);
        var liabilityAssets = await _assets.GetItemsByTypeAsync(FinancialType.Liability).ConfigureAwait(false);

        var assetRows = new List<StatementRow>();
        foreach (var item in cashAssets.Where(a => a.IsActive))
        {
            var bal = ComputeCashBalance(item.Id, tradesUntil);
            if (bal == 0m) continue;
            var converted = await ConvertOrSelfAsync(bal, item.Currency, asOf, ct).ConfigureAwait(false);
            assetRows.Add(new StatementRow(item.Name, converted, "Cash"));
        }

        // 投資市值 (snapshot 優先)
        if (_snapshots is not null)
        {
            var snap = await _snapshots.GetSnapshotAsync(asOf).ConfigureAwait(false);
            if (snap is not null && snap.MarketValue != 0m)
                assetRows.Add(new StatementRow("Portfolio", snap.MarketValue, "Investments"));
        }

        var assetTotal = assetRows.Sum(r => r.Amount);

        var liabilityRows = new List<StatementRow>();
        foreach (var item in liabilityAssets.Where(a => a.IsActive))
        {
            var bal = ComputeLiabilityBalance(item, tradesUntil);
            if (bal == 0m) continue;
            var converted = await ConvertOrSelfAsync(bal, item.Currency, asOf, ct).ConfigureAwait(false);
            liabilityRows.Add(new StatementRow(item.Name, converted, item.IsCreditCard ? "Credit Card" : "Loan"));
        }
        var liabilityTotal = liabilityRows.Sum(r => r.Amount);

        return new BalanceSheet(
            AsOf: asOf,
            Assets: new StatementSection("Assets", assetRows, assetTotal),
            Liabilities: new StatementSection("Liabilities", liabilityRows, liabilityTotal),
            NetWorth: assetTotal - liabilityTotal);
    }

    /// <summary>
    /// 若 FX 服務 + base currency 已設定且來源幣別不同，換算為 base currency；否則直接回傳。
    /// 換算失敗（缺匯率）時保留原值，避免 silent zero。
    /// </summary>
    private async Task<decimal> ConvertOrSelfAsync(decimal amount, string fromCcy, DateOnly asOf, CancellationToken ct)
    {
        if (_fx is null || _baseCurrency is null) return amount;
        if (string.IsNullOrWhiteSpace(fromCcy)) return amount;
        if (string.Equals(fromCcy, _baseCurrency, StringComparison.OrdinalIgnoreCase)) return amount;
        var converted = await _fx.ConvertAsync(amount, fromCcy, _baseCurrency, asOf, ct).ConfigureAwait(false);
        return converted ?? amount;
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
