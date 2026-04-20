using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IStockSearchService
{
    IReadOnlyList<StockSearchResult> Search(string query);
    IReadOnlyList<StockSearchResult> GetAll();
    string? GetExchange(string symbol);  // "TWSE" | "TPEX" | null
    string? GetName(string symbol);
    string? GetSector(string symbol);
    bool IsEtf(string symbol);        // true when the security type is ETF (equity or bond)
    bool IsBondEtf(string symbol);    // 債券 ETF（尾碼 B）— 證交稅免徵至 2026 底
}
