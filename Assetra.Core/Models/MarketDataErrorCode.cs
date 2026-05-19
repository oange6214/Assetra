namespace Assetra.Core.Models;

public enum MarketDataErrorCode
{
    None,
    MissingApiKey,
    QuotaExceeded,
    RateLimited,
    UnsupportedSymbol,
    ProviderUnavailable,
    NetworkFailure,
    InvalidResponse,
    CalendarClosed,
    Unknown,
}
