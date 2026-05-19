namespace Assetra.Core.Models;

public sealed record EquityInstrumentKey
{
    public EquityInstrumentKey(string symbol, string exchange)
    {
        Symbol = EquitySymbolNormalizer.NormalizeCanonicalSymbol(symbol);
        Exchange = EquitySymbolNormalizer.NormalizeExchange(exchange);

        if (Symbol.Length == 0)
            throw new ArgumentException("Instrument symbol cannot be blank.", nameof(symbol));
        if (Exchange.Length == 0)
            throw new ArgumentException("Instrument exchange cannot be blank.", nameof(exchange));
    }

    public string Symbol { get; init; }
    public string Exchange { get; init; }
    public string Value => $"{Exchange}:{Symbol}";

    public override string ToString() => Value;
}
