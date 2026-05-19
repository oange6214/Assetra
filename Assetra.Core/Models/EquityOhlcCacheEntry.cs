namespace Assetra.Core.Models;

public sealed record EquityOhlcCacheEntry(
    string Symbol,
    string Exchange,
    string Interval,
    OhlcvPoint Candle,
    string Currency,
    string SourceProvider,
    DateTimeOffset SourceUpdatedAt,
    bool IsAdjusted)
{
    public EquityInstrumentKey InstrumentKey => new(Symbol, Exchange);
}
