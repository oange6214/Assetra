namespace Assetra.Core.Models.Analysis;

/// <summary>
/// 對標標的某時點「自區間起點起算的累積報酬率」（normalized %，以小數表示，0.05 = +5%）。
/// <see cref="Date"/> 是 <c>DateTime</c>：日線＝交易日午夜、盤中（1D/5D）＝實際時分。所有標的都從 0%
/// 起跑，不同價位尺度才能同圖比較（與 Google Finance 的比較圖一致）。<see cref="Value"/> = 該點絕對值。
/// </summary>
public sealed record BenchmarkSeriesPoint(DateTime Date, decimal PercentFromStart, decimal Value = 0m);
