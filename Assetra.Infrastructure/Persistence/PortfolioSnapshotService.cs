using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// Records one portfolio snapshot per calendar day.
/// Called from PortfolioViewModel after totals are rebuilt with live prices.
/// Idempotent — the in-memory date guard prevents repeated DB writes,
/// and INSERT OR REPLACE handles app restarts within the same day.
///
/// v0.14.2: snapshots are tagged with the currency they were computed in
/// (typically <c>AppSettings.BaseCurrency</c>) so downstream analysis (MWR, trends)
/// can detect mismatches and convert via FX rather than silently mixing units.
/// </summary>
public sealed class PortfolioSnapshotService
{
    private readonly IPortfolioSnapshotRepository _repo;
    private SnapshotWriteKey? _lastSnapshotWrite;

    public PortfolioSnapshotService(IPortfolioSnapshotRepository repo)
    {
        _repo = repo;
    }

    /// <summary>
    /// Persist today's snapshot when the computed values changed this session.
    /// Skips when <paramref name="marketValue"/> is zero (prices not yet loaded).
    /// </summary>
    /// <param name="currency">
    /// The currency the totals are computed in. Defaults to "TWD" for compatibility with
    /// pre-v0.14.2 callers; new callers should pass <c>AppSettings.BaseCurrency</c>.
    /// </param>
    /// <param name="cashValue">v0.30+ optional：當日現金總額（含所有資金帳戶）。</param>
    /// <param name="equityValue">v0.30+ optional：當日投資組合市值（通常 = <paramref name="marketValue"/>）。</param>
    /// <param name="liabilityValue">v0.30+ optional：當日負債總額（信用卡 + 貸款餘額）。</param>
    public async Task<bool> TryRecordAsync(
        decimal totalCost, decimal marketValue, decimal pnl, int positionCount,
        string currency = "TWD",
        decimal? cashValue = null,
        decimal? equityValue = null,
        decimal? liabilityValue = null)
    {
        if (marketValue <= 0)
            return false;
        if (positionCount == 0)
            return false;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var normalizedCurrency = string.IsNullOrWhiteSpace(currency) ? "TWD" : currency;
        var writeKey = new SnapshotWriteKey(
            today,
            totalCost,
            marketValue,
            pnl,
            positionCount,
            normalizedCurrency,
            cashValue,
            equityValue ?? marketValue,
            liabilityValue);

        // 三個淨值組件（cash / equity / liability）持久化後，未來日損益月曆與
        // KPI bar 30 天淨值 sparkline 才能計算「真實淨值」變動而非投資 MV
        // proxy。equityValue 預設 fallback 為 marketValue（兩者通常相同）。
        if (_lastSnapshotWrite == writeKey)
            return false;

        var snapshot = new PortfolioDailySnapshot(
            today, totalCost, marketValue, pnl, positionCount,
            normalizedCurrency,
            CashValue: cashValue,
            EquityValue: equityValue ?? marketValue,
            LiabilityValue: liabilityValue);
        await _repo.UpsertAsync(snapshot).ConfigureAwait(false);
        _lastSnapshotWrite = writeKey;
        return true;
    }

    private readonly record struct SnapshotWriteKey(
        DateOnly Date,
        decimal TotalCost,
        decimal MarketValue,
        decimal Pnl,
        int PositionCount,
        string Currency,
        decimal? CashValue,
        decimal? EquityValue,
        decimal? LiabilityValue);
}
