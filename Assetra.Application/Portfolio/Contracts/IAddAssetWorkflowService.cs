using Assetra.Application.Portfolio.Dtos;
using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Contracts;

public interface IAddAssetWorkflowService
{
    IReadOnlyList<StockSearchResult> SearchSymbols(string query, int maxResults = 8);
    Task<ClosePriceLookupResult> LookupClosePriceAsync(
        string symbol,
        DateTime buyDate,
        string? exchange = null,
        CancellationToken ct = default);
    BuyPreviewResult BuildBuyPreview(BuyPreviewRequest request);
    Task<PortfolioEntry> EnsureStockEntryAsync(EnsureStockEntryRequest request, CancellationToken ct = default);
    Task<StockBuyResult> ExecuteStockBuyAsync(StockBuyRequest request, CancellationToken ct = default);
    Task<ManualAssetCreateResult> CreateManualAssetAsync(ManualAssetCreateRequest request, CancellationToken ct = default);
    string InferExchange(string symbol);

    /// <summary>
    /// 純讀取地判定一個代號在 symbol-directory 的「即時報價可用性」狀態，供
    /// 新增投資對話框顯示非阻擋式提示。不寫入任何資料、不觸發網路下載。
    /// </summary>
    WatchlistSymbolReadiness CheckWatchlistSymbol(string symbol, string? exchange = null);
}
