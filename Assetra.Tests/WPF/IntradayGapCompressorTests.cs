using Assetra.WPF.Features.Portfolio;
using LiveChartsCore.Defaults;
using Xunit;

namespace Assetra.Tests.WPF;

public sealed class IntradayGapCompressorTests
{
    [Fact]
    public void Build_CompressesNonTradingGaps_ToConsecutiveSyntheticMinutes()
    {
        // 兩個 session 中間隔週末/假日（06/18 → 06/22，4 天沒開盤）→ 合成時間仍連續、gap 被壓掉；可映回真實時間。
        var d18 = new DateTime(2026, 6, 18, 9, 0, 0);
        var d22 = new DateTime(2026, 6, 22, 9, 0, 0);
        IReadOnlyList<DateTimePoint> line =
        [
            new(d18, 0d),
            new(d18.AddMinutes(1), 1d),
            new(d22, 2d),
        ];

        var (toSynthetic, realTimes) = IntradayGapCompressor.Build([line]);

        Assert.Equal(3, realTimes.Count);
        Assert.Equal(IntradayGapCompressor.SyntheticBase, toSynthetic(d18));
        Assert.Equal(IntradayGapCompressor.SyntheticBase.AddMinutes(1), toSynthetic(d18.AddMinutes(1)));
        // 跨假日仍只 +1 分鐘（4 天 gap 被壓掉），而非真實的 4 天 → 圖上不會出現長直線。
        Assert.Equal(IntradayGapCompressor.SyntheticBase.AddMinutes(2), toSynthetic(d22));
        // 映回真實時間正確（軸標籤／as-of 用）。
        Assert.Equal(d18, IntradayGapCompressor.ToReal(IntradayGapCompressor.SyntheticBase, realTimes));
        Assert.Equal(d22, IntradayGapCompressor.ToReal(IntradayGapCompressor.SyntheticBase.AddMinutes(2), realTimes));
    }

    [Fact]
    public void Remap_MovesPointsToSyntheticTime_PreservingValues()
    {
        var t = new DateTime(2026, 6, 22, 9, 0, 0);
        IReadOnlyList<DateTimePoint> line = [new(t, 5d), new(t.AddMinutes(1), 6d)];
        var (toSynthetic, _) = IntradayGapCompressor.Build([line]);

        var remapped = IntradayGapCompressor.Remap(line, toSynthetic);

        Assert.Equal(2, remapped.Count);
        Assert.Equal(IntradayGapCompressor.SyntheticBase, remapped[0].DateTime);
        Assert.Equal(5d, remapped[0].Value);
        Assert.Equal(6d, remapped[1].Value);
    }
}
