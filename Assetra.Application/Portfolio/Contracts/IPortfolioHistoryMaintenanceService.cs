namespace Assetra.Application.Portfolio.Contracts;

public interface IPortfolioHistoryMaintenanceService
{
    /// <summary>
    /// Record today's portfolio snapshot. <paramref name="currency"/> tags the totals'
    /// unit so downstream analysis can detect mismatches with the active base currency.
    /// </summary>
    /// <param name="cashValue">v0.30+：當日現金總額（IPortfolioPositionFeed.TotalCash）。為 null 時 sparkline 仍用 marketValue 作 proxy。</param>
    /// <param name="liabilityValue">v0.30+：當日負債總額（IPortfolioPositionFeed.TotalLiabilities）。</param>
    Task<bool> TryRecordSnapshotAsync(
        decimal totalCost,
        decimal marketValue,
        decimal pnl,
        int positionCount,
        string currency = "TWD",
        decimal? cashValue = null,
        decimal? liabilityValue = null,
        CancellationToken ct = default);

    Task<int> BackfillAsync(CancellationToken ct = default);

    /// <summary>
    /// 強制用 history price 重新計算 <paramref name="date"/> 那一日的 snapshot 並 UPSERT。
    /// 給「資產趨勢顯示一日跳水但其實是 partial-price snapshot 假象」的修復按鈕用。
    /// </summary>
    Task<bool> RepairSnapshotAsync(DateOnly date, CancellationToken ct = default);
}
