using Assetra.AppLayer.Portfolio.Dtos;
using Assetra.Core.Models;

namespace Assetra.AppLayer.Portfolio.Contracts;

public interface IAddAssetWorkflowService
{
    IReadOnlyList<StockSearchResult> SearchSymbols(string query, int maxResults = 8);
    Task<ClosePriceLookupResult> LookupClosePriceAsync(string symbol, DateTime buyDate, CancellationToken ct = default);
    BuyPreviewResult BuildBuyPreview(BuyPreviewRequest request);
    string InferExchange(string symbol);
}
