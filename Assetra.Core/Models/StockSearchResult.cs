namespace Assetra.Core.Models;

public record StockSearchResult(
    string Symbol,
    string Name,
    string Exchange,
    string Sector = "",
    bool IsEtf = false);
