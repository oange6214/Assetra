namespace Assetra.Infrastructure.Persistence;

/// <summary>
/// 單一交易日歷史快照重建的結果。<see cref="OldMarketValue"/> 為重建前既有 snapshot 的
/// 投資市值（無既有列時為 null），其餘 New* 欄位為重建算出的值（被 skip 時為 null）。
/// </summary>
public sealed record SnapshotRebuildDayResult(
    DateOnly Date,
    decimal? OldMarketValue,
    decimal? NewMarketValue,
    decimal? NewCash,
    decimal? NewEquity,
    decimal? NewLiability,
    RebuildDayStatus Status);

/// <summary>
/// <see cref="PortfolioSnapshotRebuildService.RebuildAsync"/> 的彙整結果：每日明細 +
/// 各狀態的計數，方便 caller（未來的 UI / migration runner）一次掌握全貌。
/// </summary>
public sealed record SnapshotRebuildReport(
    DateOnly From,
    DateOnly To,
    bool DryRun,
    IReadOnlyList<SnapshotRebuildDayResult> Days)
{
    /// <summary>實際（或 dry-run 模式下「將會」）寫入的天數。</summary>
    public int RebuiltCount => Days.Count(d => d.Status == RebuildDayStatus.Rebuilt);

    /// <summary>因既有 snapshot 已含 breakdown 而保留不動的天數。</summary>
    public int PreservedCount => Days.Count(d => d.Status == RebuildDayStatus.SkippedHasCompleteLiveRow);

    /// <summary>因任一持倉缺歷史收盤價而整日跳過的天數。</summary>
    public int UnpriceableCount => Days.Count(d => d.Status == RebuildDayStatus.SkippedUnpriceable);

    /// <summary>因任一金額缺當日匯率而整日跳過的天數。</summary>
    public int NoFxCount => Days.Count(d => d.Status == RebuildDayStatus.SkippedNoFx);

    /// <summary>當日無任何持倉（reconstructed positions 為空）的天數。</summary>
    public int NoPositionsCount => Days.Count(d => d.Status == RebuildDayStatus.SkippedNoPositions);
}

/// <summary>單一交易日的重建判定結果。</summary>
public enum RebuildDayStatus
{
    /// <summary>成功重算並（非 dry-run 時）寫入完整 breakdown snapshot。</summary>
    Rebuilt,

    /// <summary>既有 snapshot 已含 cash/equity/liability 任一非 null（live row），保留不覆蓋。</summary>
    SkippedHasCompleteLiveRow,

    /// <summary>至少一檔持倉在當日缺歷史收盤價 — all-or-nothing，整日不寫。</summary>
    SkippedUnpriceable,

    /// <summary>至少一筆金額（equity / cash / liability）在當日無可用匯率 — 整日不寫。</summary>
    SkippedNoFx,

    /// <summary>當日重建後無任何持倉。</summary>
    SkippedNoPositions,
}
