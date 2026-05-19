using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface ITwelveDataConnectionTester
{
    Task<MarketDataResult<EquityQuote>> TestAsync(string apiKey, CancellationToken ct = default);
}
