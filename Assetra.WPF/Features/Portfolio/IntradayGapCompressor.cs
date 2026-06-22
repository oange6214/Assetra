using LiveChartsCore.Defaults;

namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// 把盤中分時點的「真實時間」壓縮成連續的合成時間，移除夜盤／週末／假日空檔，讓 1D/5D 比較圖像 Google
/// 那樣 sessions 接在一起、不留長直線 gap。合成時間 = <see cref="SyntheticBase"/> + 索引分鐘；軸標籤／準星／
/// hover 再用 <see cref="ToReal"/> 映回真實時間顯示。所有線 ＋ abs 共用同一份排序後的真實時間（索引一致才對齊）。
/// </summary>
public static class IntradayGapCompressor
{
    public static readonly DateTime SyntheticBase = new(2000, 1, 1);

    /// <summary>由所有點集合建立「真實時間 → 合成時間」轉換 ＋ 排序後的真實時間清單（供映回）。</summary>
    public static (Func<DateTime, DateTime> ToSynthetic, IReadOnlyList<DateTime> RealTimes) Build(
        IEnumerable<IReadOnlyList<DateTimePoint>> pointSets)
    {
        var realSet = new SortedSet<DateTime>();
        foreach (var pts in pointSets)
            foreach (var p in pts)
                realSet.Add(p.DateTime);

        var realTimes = realSet.ToList();
        var index = new Dictionary<DateTime, int>(realTimes.Count);
        for (var i = 0; i < realTimes.Count; i++)
            index[realTimes[i]] = i;

        Func<DateTime, DateTime> toSynthetic = real =>
            index.TryGetValue(real, out var i) ? SyntheticBase.AddMinutes(i) : SyntheticBase;
        return (toSynthetic, realTimes);
    }

    /// <summary>合成時間 → 最近的真實時間（軸標籤／hover 映回顯示用）。</summary>
    public static DateTime ToReal(DateTime synthetic, IReadOnlyList<DateTime> realTimes)
    {
        if (realTimes.Count == 0)
            return synthetic;
        var i = (int)Math.Round((synthetic - SyntheticBase).TotalMinutes);
        i = Math.Clamp(i, 0, realTimes.Count - 1);
        return realTimes[i];
    }

    /// <summary>把一條線的點從真實時間搬到合成時間。</summary>
    public static IReadOnlyList<DateTimePoint> Remap(
        IReadOnlyList<DateTimePoint> points, Func<DateTime, DateTime> toSynthetic) =>
        points.Select(p => new DateTimePoint(toSynthetic(p.DateTime), p.Value)).ToList();
}
