namespace Assetra.Core.Models;

/// <summary>
/// A historical FX rate snapshot: <c>1 unit of <paramref name="From"/> = <paramref name="Rate"/> units of <paramref name="To"/></c>.
/// </summary>
public sealed record FxRate(string From, string To, decimal Rate, DateOnly AsOfDate)
{
    /// <summary>Inverted pair, useful when callers query the reverse direction.</summary>
    public FxRate Inverse() => new(To, From, Rate == 0m ? 0m : 1m / Rate, AsOfDate);
}
