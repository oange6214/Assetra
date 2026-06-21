using Assetra.Core.Models.Analysis;

namespace Assetra.Core.Interfaces.Analysis;

/// <summary>
/// 計算「投資組合群組（bucket）」的同期 % TWR 序列（與 benchmark / 我的投組同基準），供資產趨勢比較圖用。
/// 以群組的交易逐日重建持倉（Σ signed qty）× 歷史收盤 → 群組每日市值，再用 TWR 除掉買賣現金流。
/// </summary>
public interface IGroupPerformanceSeriesService
{
    /// <summary>
    /// 群組 <paramref name="groupId"/> 在 <paramref name="period"/> 的每日累積 % 序列（首日 0）；
    /// 無交易 / 不可算（缺價、序列不足）時回 <see langword="null"/>。
    /// </summary>
    Task<IReadOnlyList<BenchmarkSeriesPoint>?> ComputeGroupSeriesAsync(
        Guid groupId, PerformancePeriod period, CancellationToken ct = default);
}
