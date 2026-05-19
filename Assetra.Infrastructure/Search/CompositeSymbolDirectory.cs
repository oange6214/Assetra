using Assetra.Core.Interfaces;
using Assetra.Core.Models;

namespace Assetra.Infrastructure.Search;

public sealed class CompositeSymbolDirectory(IReadOnlyList<ISymbolDirectory> directories) : ISymbolDirectory
{
    public IReadOnlyList<StockSearchResult> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var normalizedQuery = EquitySymbolNormalizer.ToSearchKey(query);
        return directories
            .SelectMany(d => d.Search(query))
            .GroupBy(r => (Symbol: r.Symbol.ToUpperInvariant(), Exchange: r.Exchange.ToUpperInvariant()))
            .Select(g => g.First())
            .OrderByDescending(r => string.Equals(
                EquitySymbolNormalizer.ToSearchKey(r.Symbol),
                normalizedQuery,
                StringComparison.OrdinalIgnoreCase))
            .ThenBy(r => r.Symbol)
            .ThenBy(r => r.Exchange)
            .Take(25)
            .ToList();
    }

    public StockSearchResult? Resolve(string symbol, string? exchange = null)
    {
        foreach (var directory in directories)
        {
            var result = directory.Resolve(symbol, exchange);
            if (result is not null)
                return result;
        }

        return null;
    }
}
