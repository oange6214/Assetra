namespace Assetra.Core.Models.Analysis;

/// <summary>
/// 對標標的某交易日「自區間起點起算的累積報酬率」（normalized %，以小數表示，0.05 = +5%）。
/// 用於資產趨勢圖的 benchmark 疊線：所有標的都從 0% 起跑，不同價位尺度才能同圖比較
/// （與 Google Finance 的比較圖一致）。
/// </summary>
public sealed record BenchmarkSeriesPoint(DateOnly Date, decimal PercentFromStart, decimal Value = 0m);
