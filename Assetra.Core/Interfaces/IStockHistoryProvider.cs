using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface IStockHistoryProvider
{
    Task<IReadOnlyList<OhlcvPoint>> GetHistoryAsync(string symbol, string exchange, ChartPeriod period, CancellationToken ct = default);
}
