using Assetra.Core.Models;

namespace Assetra.Infrastructure.Http;

internal interface ITwseClient
{
    Task<IReadOnlyList<StockQuote>> FetchQuotesAsync(IEnumerable<string> symbols);
}
