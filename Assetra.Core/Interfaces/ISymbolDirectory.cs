using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface ISymbolDirectory
{
    IReadOnlyList<StockSearchResult> Search(string query);
    StockSearchResult? Resolve(string symbol, string? exchange = null);
}
