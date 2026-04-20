using Assetra.Core.Models;

namespace Assetra.Infrastructure.Http;

internal interface ITpexClient
{
    Task<IReadOnlyList<StockQuote>> FetchQuotesAsync(IEnumerable<string> symbols);
}
