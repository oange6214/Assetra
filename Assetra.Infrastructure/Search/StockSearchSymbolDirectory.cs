using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Search;

public sealed class StockSearchSymbolDirectory(IStockSearchService searchService) : ISymbolDirectory
{
    public IReadOnlyList<StockSearchResult> Search(string query) =>
        searchService.Search(query);

    public StockSearchResult? Resolve(string symbol, string? exchange = null)
    {
        var canonical = EquitySymbolNormalizer.NormalizeCanonicalSymbol(symbol);
        var normalizedExchange = EquitySymbolNormalizer.NormalizeExchange(exchange ?? string.Empty);
        if (canonical.Length == 0)
            return null;

        return searchService
            .GetAll()
            .FirstOrDefault(s =>
                EquitySymbolNormalizer.SymbolMatches(s.Symbol, canonical) &&
                (normalizedExchange.Length == 0 ||
                 string.Equals(s.Exchange, normalizedExchange, StringComparison.OrdinalIgnoreCase)));
    }
}
