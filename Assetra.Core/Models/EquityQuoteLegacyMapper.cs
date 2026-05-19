namespace Assetra.Core.Models;

public static class EquityQuoteLegacyMapper
{
    public static StockQuote ToStockQuote(
        EquityQuote quote,
        string name = "",
        long volume = 0,
        decimal? open = null,
        decimal? high = null,
        decimal? low = null)
    {
        ArgumentNullException.ThrowIfNull(quote);

        var previousClose = quote.PreviousClose ?? 0m;
        var change = quote.Change ?? (previousClose > 0 ? quote.Price - previousClose : 0m);
        var changePercent = quote.ChangePercent ?? (previousClose > 0 ? change / previousClose * 100m : 0m);

        return new StockQuote(
            quote.Instrument.Symbol,
            string.IsNullOrWhiteSpace(name) ? quote.Name : name,
            quote.Instrument.Exchange,
            quote.Price,
            change,
            changePercent,
            volume,
            open ?? quote.Price,
            high ?? (previousClose > 0 ? Math.Max(quote.Price, previousClose) : quote.Price),
            low ?? (previousClose > 0 ? Math.Min(quote.Price, previousClose) : quote.Price),
            previousClose,
            quote.UpdatedAt,
            quote.Currency,
            quote.IsStale,
            quote.ProviderStateMessage);
    }
}
