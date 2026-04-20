using Assetra.Core.Models;

namespace Assetra.Infrastructure.Chart;

internal static class ChartCalculator
{
    public static IReadOnlyList<decimal?> CalculateMa(IReadOnlyList<decimal> closes, int period)
    {
        var result = new decimal?[closes.Count];
        for (int i = 0; i < closes.Count; i++)
        {
            if (i < period - 1)
            { result[i] = null; continue; }
            decimal sum = 0;
            for (int j = i - period + 1; j <= i; j++)
                sum += closes[j];
            result[i] = sum / period;
        }
        return result;
    }

    public static MacdData CalculateMacd(IReadOnlyList<decimal> closes, int fast = 12, int slow = 26, int signal = 9)
    {
        var emaFast = CalculateEma(closes, fast);
        var emaSlow = CalculateEma(closes, slow);
        var dif = new decimal?[closes.Count];
        for (int i = 0; i < closes.Count; i++)
            dif[i] = emaFast[i] is not null && emaSlow[i] is not null
                ? emaFast[i]! - emaSlow[i]!
                : null;
        var difValues = dif.Select(v => v ?? 0m).ToList();
        var signalLine = CalculateEmaFromValues(difValues, signal, firstNonNullIndex: slow - 1);
        var histogram = new decimal?[closes.Count];
        for (int i = 0; i < closes.Count; i++)
            histogram[i] = dif[i] is not null && signalLine[i] is not null
                ? dif[i]! - signalLine[i]!
                : null;
        return new MacdData(dif, signalLine, histogram);
    }

    /// <summary>Wilder RSI. Returns null for the first <paramref name="period"/> bars.</summary>
    public static IReadOnlyList<decimal?> CalculateRsi(IReadOnlyList<decimal> closes, int period = 14)
    {
        var result = new decimal?[closes.Count];
        if (closes.Count < period + 1)
            return result;

        decimal avgGain = 0, avgLoss = 0;
        for (int i = 1; i <= period; i++)
        {
            var d = closes[i] - closes[i - 1];
            if (d > 0)
                avgGain += d;
            else
                avgLoss += -d;
        }
        avgGain /= period;
        avgLoss /= period;
        result[period] = avgLoss == 0 ? 100m : 100m - 100m / (1m + avgGain / avgLoss);

        for (int i = period + 1; i < closes.Count; i++)
        {
            var d = closes[i] - closes[i - 1];
            var gain = d > 0 ? d : 0m;
            var loss = d < 0 ? -d : 0m;
            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
            result[i] = avgLoss == 0 ? 100m : 100m - 100m / (1m + avgGain / avgLoss);
        }
        return result;
    }

    /// <summary>Bollinger Bands using population standard deviation.</summary>
    public static BollingerBandsData CalculateBollingerBands(
        IReadOnlyList<decimal> closes, int period = 20, decimal multiplier = 2.0m)
    {
        var n = closes.Count;
        var upper = new decimal?[n];
        var middle = new decimal?[n];
        var lower = new decimal?[n];

        for (int i = period - 1; i < n; i++)
        {
            decimal sum = 0;
            for (int j = i - period + 1; j <= i; j++)
                sum += closes[j];
            var sma = sum / period;

            decimal variance = 0;
            for (int j = i - period + 1; j <= i; j++)
            {
                var diff = closes[j] - sma;
                variance += diff * diff;
            }
            var stddev = (decimal)Math.Sqrt((double)(variance / period));

            middle[i] = sma;
            upper[i] = sma + multiplier * stddev;
            lower[i] = sma - multiplier * stddev;
        }
        return new BollingerBandsData(upper, middle, lower);
    }

    /// <summary>
    /// KD stochastic — Wilder smoothing (2/3 weight on previous value).
    /// K and D both start at 50 before the first RSV is computed.
    /// Returns null for the first <paramref name="period"/>-1 bars.
    /// </summary>
    public static KdData CalculateKd(
        IReadOnlyList<decimal> highs,
        IReadOnlyList<decimal> lows,
        IReadOnlyList<decimal> closes,
        int period = 9)
    {
        var n = closes.Count;
        var k = new decimal?[n];
        var d = new decimal?[n];
        if (n < period)
            return new KdData(k, d);

        decimal prevK = 50m, prevD = 50m;
        for (int i = period - 1; i < n; i++)
        {
            decimal highestHigh = highs[i - period + 1];
            decimal lowestLow = lows[i - period + 1];
            for (int j = i - period + 2; j <= i; j++)
            {
                if (highs[j] > highestHigh)
                    highestHigh = highs[j];
                if (lows[j] < lowestLow)
                    lowestLow = lows[j];
            }

            var range = highestHigh - lowestLow;
            var rsv = range == 0 ? 50m : (closes[i] - lowestLow) / range * 100m;
            var kVal = prevK * 2m / 3m + rsv / 3m;
            var dVal = prevD * 2m / 3m + kVal / 3m;

            k[i] = kVal;
            d[i] = dVal;
            prevK = kVal;
            prevD = dVal;
        }
        return new KdData(k, d);
    }

    private static decimal?[] CalculateEma(IReadOnlyList<decimal> values, int period)
    {
        var result = new decimal?[values.Count];
        if (values.Count < period)
            return result;
        decimal multiplier = 2m / (period + 1);
        decimal sum = 0;
        for (int i = 0; i < period; i++)
            sum += values[i];
        result[period - 1] = sum / period;
        for (int i = period; i < values.Count; i++)
            result[i] = (values[i] - result[i - 1]!.Value) * multiplier + result[i - 1]!.Value;
        return result;
    }

    private static decimal?[] CalculateEmaFromValues(IList<decimal> values, int period, int firstNonNullIndex)
    {
        var result = new decimal?[values.Count];
        int start = firstNonNullIndex + period - 1;
        if (start >= values.Count)
            return result;
        decimal multiplier = 2m / (period + 1);
        decimal sum = 0;
        for (int i = firstNonNullIndex; i <= start; i++)
            sum += values[i];
        result[start] = sum / period;
        for (int i = start + 1; i < values.Count; i++)
            result[i] = (values[i] - result[i - 1]!.Value) * multiplier + result[i - 1]!.Value;
        return result;
    }
}
