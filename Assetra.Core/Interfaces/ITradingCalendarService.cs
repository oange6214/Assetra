using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface ITradingCalendarService
{
    TradingDayKind GetTradingDayKind(string exchange, DateOnly localDate);

    bool ShouldRefreshQuotes(string exchange, DateTimeOffset utcNow);
}
