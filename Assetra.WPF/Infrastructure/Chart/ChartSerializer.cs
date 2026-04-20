using Assetra.Core.Models;

namespace Assetra.WPF.Infrastructure.Chart;

/// <summary>
/// 將 C# <see cref="ChartData"/>（decimal?[]）轉換成 JS 可接受的匿名物件，
/// 再由呼叫端透過 System.Text.Json 序列化成 JSON 字串。
/// </summary>
public static class ChartSerializer
{
    public static object Serialize(ChartData data)
    {
        var candles = data.Candles
            .Select(c => new
            {
                time = c.Date.ToString("yyyy-MM-dd"),
                open = (double)c.Open,
                high = (double)c.High,
                low = (double)c.Low,
                close = (double)c.Close,
                volume = c.Volume,
            })
            .ToList();

        return new
        {
            candles,
            ma5 = ToDoubles(data.Ma5),
            ma20 = ToDoubles(data.Ma20),
            ma60 = ToDoubles(data.Ma60),
            bb = new
            {
                upper = ToDoubles(data.BollingerBands.Upper),
                middle = ToDoubles(data.BollingerBands.Middle),
                lower = ToDoubles(data.BollingerBands.Lower),
            },
            rsi = ToDoubles(data.Rsi),
            kd = new
            {
                k = ToDoubles(data.Kd.K),
                d = ToDoubles(data.Kd.D),
            },
            macd = new
            {
                dif = ToDoubles(data.Macd.Dif),
                signal = ToDoubles(data.Macd.Signal),
                hist = ToDoubles(data.Macd.Histogram),
            },
            volumeMa5 = ToDoubles(data.VolumeMA5),
            volumeMa20 = ToDoubles(data.VolumeMA20),
        };
    }

    private static double?[] ToDoubles(IReadOnlyList<decimal?> src) =>
        src.Select(v => v.HasValue ? (double?)((double)v.Value) : null).ToArray();
}
