namespace Assetra.Core.Models;

public record MacdData(
    IReadOnlyList<decimal?> Dif,
    IReadOnlyList<decimal?> Signal,
    IReadOnlyList<decimal?> Histogram);

public record BollingerBandsData(
    IReadOnlyList<decimal?> Upper,
    IReadOnlyList<decimal?> Middle,
    IReadOnlyList<decimal?> Lower);

public record KdData(
    IReadOnlyList<decimal?> K,
    IReadOnlyList<decimal?> D);

public record ChartData(
    IReadOnlyList<OhlcvPoint> Candles,
    IReadOnlyList<decimal?> Ma5,
    IReadOnlyList<decimal?> Ma20,
    IReadOnlyList<decimal?> Ma60,
    MacdData Macd,
    IReadOnlyList<decimal?> Rsi,
    BollingerBandsData BollingerBands,
    IReadOnlyList<decimal?> VolumeMA5,
    IReadOnlyList<decimal?> VolumeMA20,
    KdData Kd);
