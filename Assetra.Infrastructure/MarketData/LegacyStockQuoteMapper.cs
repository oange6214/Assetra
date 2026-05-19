using Assetra.Core.Models;

namespace Assetra.Infrastructure.MarketData;

internal static class LegacyStockQuoteMapper
{
    public static MarketDataResult<EquityQuote> ToEquityQuoteResult(
        StockQuote quote,
        string providerName,
        bool isDelayed = false)
    {
        var instrument = new EquityInstrumentKey(quote.Symbol, quote.Exchange);
        var currency = StockExchangeRegistry.ResolveDefaultCurrency(quote.Exchange);
        var equityQuote = new EquityQuote(
            instrument,
            quote.Price,
            quote.PrevClose,
            quote.Change,
            quote.ChangePercent,
            currency,
            quote.UpdatedAt,
            providerName,
            isDelayed,
            quote.Name);

        return MarketDataResult<EquityQuote>.Success(equityQuote);
    }

    public static MarketDataResult<EquityQuote> MissingQuote(
        EquityInstrumentKey key,
        string providerName,
        string message)
    {
        return MarketDataResult<EquityQuote>.Failure(new MarketDataError(
            MarketDataErrorCode.UnsupportedSymbol,
            message,
            Provider: providerName,
            Instrument: key));
    }
}
