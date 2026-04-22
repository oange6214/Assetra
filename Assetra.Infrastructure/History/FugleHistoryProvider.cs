using Assetra.Core.Interfaces;
using Assetra.Core.Models;
using Assetra.Infrastructure.Http;

namespace Assetra.Infrastructure.History;

internal sealed class FugleHistoryProvider(FugleClient fugleClient) : IStockHistoryProvider
{
    public Task<IReadOnlyList<OhlcvPoint>> GetHistoryAsync(
        string symbol, string exchange, ChartPeriod period, CancellationToken ct = default)
    {
        return fugleClient.FetchDailyHistoryAsync(symbol, period, ct);
    }
}
