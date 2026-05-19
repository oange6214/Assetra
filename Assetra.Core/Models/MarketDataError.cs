namespace Assetra.Core.Models;

public sealed record MarketDataError(
    MarketDataErrorCode Code,
    string Message,
    string? Provider = null,
    EquityInstrumentKey? Instrument = null,
    bool IsRetryable = false)
{
    public bool IsQuotaRelated => Code is MarketDataErrorCode.QuotaExceeded or MarketDataErrorCode.RateLimited;
}
