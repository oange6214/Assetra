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
/// </remarks>
public sealed class BalanceQueryService : IBalanceQueryService
{
    private readonly ITradeRepository _trades;

    public BalanceQueryService(ITradeRepository trades)
    {
        _trades = trades ?? throw new ArgumentNullException(nameof(trades));
    }

    public async Task<decimal> GetCashBalanceAsync(Guid cashAccountId)
    {
        var trades = await _trades.GetAllAsync().ConfigureAwait(false);
        return ComputeCashBalance(cashAccountId, trades);
    }

    public async Task<LiabilitySnapshot> GetLiabilitySnapshotAsync(string loanLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loanLabel);
        var trades = await _trades.GetAllAsync().ConfigureAwait(false);
        return ComputeLiabilitySnapshot(loanLabel, trades);
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetAllCashBalancesAsync()
    {
        var trades = await _trades.GetAllAsync().ConfigureAwait(false);
        var result = new Dictionary<Guid, decimal>();
        foreach (var t in trades)
        {
            if (t.CashAccountId is { } src)
            {
                var delta = PrimaryCashDelta(t);
                if (delta != 0) Accumulate(result, src, delta);
            }
            if (t.Type == TradeType.Transfer && t.ToCashAccountId is { } dst)
            {
                Accumulate(result, dst, t.CashAmount ?? 0m);
            }
        }
        return result;
    }

    public async Task<IReadOnlyDictionary<string, LiabilitySnapshot>> GetAllLiabilitySnapshotsAsync()
    {
        var trades = await _trades.GetAllAsync().ConfigureAwait(false);
        var result = new Dictionary<string, LiabilitySnapshot>(StringComparer.Ordinal);
        foreach (var t in trades)
        {
            if (string.IsNullOrWhiteSpace(t.LoanLabel)) continue;
            var label = t.LoanLabel;
            var current = result.TryGetValue(label, out var snap) ? snap : LiabilitySnapshot.Empty;
            result[label] = t.Type switch
            {
                TradeType.LoanBorrow => new LiabilitySnapshot(
                    current.Balance + (t.CashAmount ?? 0m),
                    current.OriginalAmount + (t.CashAmount ?? 0m)),
                TradeType.LoanRepay => new LiabilitySnapshot(
                    current.Balance - (t.Principal ?? t.CashAmount ?? 0m),
                    current.OriginalAmount),
                _ => current,
            };
        }
        return result;
    }

    // ─── Pure projection helpers ─────────────────────────────────────────────

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
    /// 計算指定貸款名稱的 (Balance, OriginalAmount)。
    /// </summary>
    internal static LiabilitySnapshot ComputeLiabilitySnapshot(
        string loanLabel, IReadOnlyList<Trade> trades)
    {
        decimal balance = 0m, original = 0m;
        foreach (var t in trades)
        {
            if (!string.Equals(t.LoanLabel, loanLabel, StringComparison.Ordinal)) continue;
            switch (t.Type)
            {
                case TradeType.LoanBorrow:
                {
                    var amt = t.CashAmount ?? 0m;
                    balance  += amt;
                    original += amt;
                    break;
                }
                case TradeType.LoanRepay:
                    balance -= t.Principal ?? t.CashAmount ?? 0m;
                    break;
            }
        }
        return new LiabilitySnapshot(balance, original);
    }

    /// <summary>
    /// 單筆交易對「主現金帳戶」（<see cref="Trade.CashAccountId"/>）的現金增減。
    /// Transfer 的目標帳戶在上層另行處理。
    /// </summary>
    internal static decimal PrimaryCashDelta(Trade t) => t.Type switch
    {
        TradeType.Income       => +(t.CashAmount ?? 0m),
        TradeType.CashDividend => +(t.CashAmount ?? 0m),
        TradeType.Deposit      => +(t.CashAmount ?? 0m),
        TradeType.Withdrawal   => -(t.CashAmount ?? 0m),
        TradeType.LoanBorrow   => +(t.CashAmount ?? 0m) - (t.Commission ?? 0m),
        TradeType.LoanRepay    => -(t.CashAmount ?? 0m),   // 全部付款從現金扣
        TradeType.Transfer     => -(t.CashAmount ?? 0m),   // 來源帳戶減少
        TradeType.Buy          => -(t.Price * t.Quantity + (t.Commission ?? 0m)),
        TradeType.Sell         => +(t.Price * t.Quantity - (t.Commission ?? 0m)),
        _                      => 0m,
    };

    private static void Accumulate(Dictionary<Guid, decimal> map, Guid key, decimal delta)
    {
        map[key] = (map.TryGetValue(key, out var v) ? v : 0m) + delta;
    }
}
