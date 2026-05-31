using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure;

/// <inheritdoc cref="IBalanceQueryService"/>
///
/// <remarks>
/// 純投影實作：所有方法都只讀取 <see cref="ITradeRepository"/>，不修改任何狀態。
/// 單次呼叫的成本 = O(N) N = 交易筆數。對於 UI 載入清單，使用
/// <see cref="GetAllCashBalancesAsync"/> / <see cref="GetAllLiabilitySnapshotsAsync"/>
/// 能將整張表一次投影完成，避免 O(N × M) 的重複掃描。
///
/// <para>
/// M1 — 注入 <see cref="IAssetRepository"/> 為了取得每個 cash account 的幣別 tag，
/// 使回傳的 <see cref="Money"/> 自帶正確幣別。<see cref="IAssetRepository"/> 為 optional：
/// 未注入時所有結果一律 tag 為 <see cref="IBalanceQueryService.DefaultCurrency"/>，
/// 便於現有測試最小成本通過。
/// </para>
/// </remarks>
public sealed class BalanceQueryService : IBalanceQueryService
{
    private readonly ITradeRepository _trades;
    private readonly IAssetRepository? _assets;

    public BalanceQueryService(ITradeRepository trades, IAssetRepository? assets = null)
    {
        _trades = trades ?? throw new ArgumentNullException(nameof(trades));
        _assets = assets;
    }

    public async Task<Money> GetCashBalanceAsync(Guid cashAccountId)
    {
        var trades = await _trades.GetAllAsync().ConfigureAwait(false);
        var amount = ComputeCashBalance(cashAccountId, trades);
        var currency = await ResolveCurrencyAsync(cashAccountId).ConfigureAwait(false);
        return new Money(amount, currency);
    }

    public async Task<LiabilitySnapshot> GetLiabilitySnapshotAsync(string loanLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loanLabel);
        var trades = await _trades.GetAllAsync().ConfigureAwait(false);
        var (balance, original) = ComputeLiabilitySnapshot(loanLabel, trades);
        var currency = await ResolveLiabilityCurrencyAsync(loanLabel).ConfigureAwait(false);
        return new LiabilitySnapshot(new Money(balance, currency), new Money(original, currency));
    }

    public async Task<IReadOnlyDictionary<Guid, Money>> GetAllCashBalancesAsync()
    {
        var trades = await _trades.GetAllAsync().ConfigureAwait(false);
        var currencyMap = await BuildCurrencyMapAsync().ConfigureAwait(false);
        var amounts = new Dictionary<Guid, decimal>();
        foreach (var t in trades)
        {
            if (t.CashAccountId is { } src)
            {
                var delta = PrimaryCashDelta(t);
                if (delta != 0)
                    Accumulate(amounts, src, delta);
            }
            if (t.Type == TradeType.Transfer && t.ToCashAccountId is { } dst)
            {
                Accumulate(amounts, dst, t.CashAmount ?? 0m);
            }
        }
        var result = new Dictionary<Guid, Money>(amounts.Count);
        foreach (var (id, amt) in amounts)
        {
            var ccy = currencyMap.TryGetValue(id, out var c) ? c : IBalanceQueryService.DefaultCurrency;
            result[id] = new Money(amt, ccy);
        }
        return result;
    }

    public async Task<IReadOnlyDictionary<string, LiabilitySnapshot>> GetAllLiabilitySnapshotsAsync()
    {
        var trades = await _trades.GetAllAsync().ConfigureAwait(false);
        var liabilityCurrencies = await BuildLiabilityCurrencyMapAsync().ConfigureAwait(false);
        // First pass: accumulate raw decimals so arithmetic stays in one currency.
        var amounts = new Dictionary<string, (decimal Balance, decimal Original)>(StringComparer.Ordinal);
        foreach (var t in trades)
        {
            var label = GetLiabilityLabel(t);
            if (string.IsNullOrWhiteSpace(label))
                continue;
            (decimal Balance, decimal Original) current = amounts.TryGetValue(label, out var snap) ? snap : (0m, 0m);
            amounts[label] = t.Type switch
            {
                TradeType.LoanBorrow => (
                    current.Balance + (t.CashAmount ?? 0m),
                    current.Original + (t.CashAmount ?? 0m)),
                TradeType.LoanRepay => (
                    current.Balance - (t.Principal ?? t.CashAmount ?? 0m),
                    current.Original),
                TradeType.CreditCardCharge => (
                    current.Balance + (t.CashAmount ?? 0m),
                    current.Original + (t.CashAmount ?? 0m)),
                TradeType.CreditCardPayment => (
                    current.Balance - (t.CashAmount ?? 0m),
                    current.Original),
                _ => current,
            };
        }
        var result = new Dictionary<string, LiabilitySnapshot>(StringComparer.Ordinal);
        foreach (var kv in amounts)
        {
            var ccy = liabilityCurrencies.TryGetValue(kv.Key, out var c) ? c : IBalanceQueryService.DefaultCurrency;
            result[kv.Key] = new LiabilitySnapshot(new Money(kv.Value.Balance, ccy), new Money(kv.Value.Original, ccy));
        }
        return result;
    }

    // ─── Currency lookup helpers ─────────────────────────────────────────────

    private async Task<string> ResolveCurrencyAsync(Guid cashAccountId)
    {
        if (_assets is null)
            return IBalanceQueryService.DefaultCurrency;
        var item = await _assets.GetByIdAsync(cashAccountId).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(item?.Currency) ? IBalanceQueryService.DefaultCurrency : item!.Currency;
    }

    private async Task<string> ResolveLiabilityCurrencyAsync(string loanLabel)
    {
        if (_assets is null)
            return IBalanceQueryService.DefaultCurrency;
        var liabilities = await _assets.GetItemsByTypeAsync(FinancialType.Liability).ConfigureAwait(false);
        var match = liabilities.FirstOrDefault(a => string.Equals(a.Name, loanLabel, StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(match?.Currency) ? IBalanceQueryService.DefaultCurrency : match!.Currency;
    }

    private async Task<IReadOnlyDictionary<Guid, string>> BuildCurrencyMapAsync()
    {
        if (_assets is null)
            return new Dictionary<Guid, string>();
        var assets = await _assets.GetItemsByTypeAsync(FinancialType.Asset).ConfigureAwait(false);
        return assets
            .Where(a => !string.IsNullOrWhiteSpace(a.Currency))
            .ToDictionary(a => a.Id, a => a.Currency);
    }

    private async Task<IReadOnlyDictionary<string, string>> BuildLiabilityCurrencyMapAsync()
    {
        if (_assets is null)
            return new Dictionary<string, string>(StringComparer.Ordinal);
        var liabs = await _assets.GetItemsByTypeAsync(FinancialType.Liability).ConfigureAwait(false);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var l in liabs)
        {
            if (string.IsNullOrWhiteSpace(l.Currency))
                continue;
            dict[l.Name] = l.Currency;
        }
        return dict;
    }

    // ─── Pure projection helpers (decimal arithmetic preserved) ──────────────

    /// <summary>
    /// 計算指定現金帳戶在整個交易歷史中的累計餘額。
    /// 帳戶 createdBalance 永遠為 0 — 開戶初值若有資金，需以 <see cref="TradeType.Deposit"/> 表達。
    /// </summary>
    internal static decimal ComputeCashBalance(Guid accountId, IReadOnlyList<Trade> trades)
    {
        decimal balance = 0m;
        foreach (var t in trades)
        {
            if (t.CashAccountId == accountId)
                balance += PrimaryCashDelta(t);
            if (t.Type == TradeType.Transfer && t.ToCashAccountId == accountId)
                balance += t.CashAmount ?? 0m;
        }
        return balance;
    }

    /// <summary>
    /// 計算指定貸款名稱的 (Balance, OriginalAmount)（純 decimal 內部使用）。
    /// </summary>
    internal static (decimal Balance, decimal Original) ComputeLiabilitySnapshot(
        string loanLabel, IReadOnlyList<Trade> trades)
    {
        decimal balance = 0m, original = 0m;
        foreach (var t in trades)
        {
            if (!string.Equals(GetLiabilityLabel(t), loanLabel, StringComparison.Ordinal))
                continue;
            switch (t.Type)
            {
                case TradeType.LoanBorrow:
                {
                    var amt = t.CashAmount ?? 0m;
                    balance += amt;
                    original += amt;
                    break;
                }
                case TradeType.LoanRepay:
                    balance -= t.Principal ?? t.CashAmount ?? 0m;
                    break;
                case TradeType.CreditCardCharge:
                {
                    var amt = t.CashAmount ?? 0m;
                    balance += amt;
                    original += amt;
                    break;
                }
                case TradeType.CreditCardPayment:
                    balance -= t.CashAmount ?? 0m;
                    break;
            }
        }
        return (balance, original);
    }

    /// <summary>
    /// 單筆交易對「主現金帳戶」（<see cref="Trade.CashAccountId"/>）的現金增減。
    /// Transfer 的目標帳戶在上層另行處理。
    /// </summary>
    internal static decimal PrimaryCashDelta(Trade t) => t.Type switch
    {
        TradeType.Income => +(t.CashAmount ?? 0m),
        TradeType.CashDividend => +(t.CashAmount ?? 0m),
        TradeType.Deposit => +(t.CashAmount ?? 0m),
        TradeType.Withdrawal => -(t.CashAmount ?? 0m),
        TradeType.LoanBorrow => +(t.CashAmount ?? 0m) - (t.Commission ?? 0m),
        TradeType.LoanRepay => -(t.CashAmount ?? 0m),   // 全部付款從現金扣
        TradeType.CreditCardPayment => -(t.CashAmount ?? 0m),
        TradeType.Transfer => -(t.CashAmount ?? 0m),   // 來源帳戶減少
        TradeType.Buy => -BuyCashAmount(t),
        TradeType.Sell => +SellCashAmount(t),
        _ => 0m,
    };

    private static decimal BuyCashAmount(Trade t) =>
        t.CashAmount ?? (t.Price * t.Quantity + (t.Commission ?? 0m));

    private static decimal SellCashAmount(Trade t) =>
        t.CashAmount ?? (t.Price * t.Quantity - (t.Commission ?? 0m));

    private static string? GetLiabilityLabel(Trade t) => t.Type switch
    {
        TradeType.LoanBorrow or TradeType.LoanRepay => t.LoanLabel,
        TradeType.CreditCardCharge or TradeType.CreditCardPayment => t.Name,
        _ => null,
    };

    private static void Accumulate(Dictionary<Guid, decimal> map, Guid key, decimal delta)
    {
        map[key] = (map.TryGetValue(key, out var v) ? v : 0m) + delta;
    }
}
