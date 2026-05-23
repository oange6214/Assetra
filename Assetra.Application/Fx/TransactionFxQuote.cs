namespace Assetra.Application.Fx;

public enum TransactionFxQuoteStatus
{
    Resolved,
    SameCurrency,
    MissingInput,
    Unavailable
}

public sealed record TransactionFxQuote(
    string FromCurrency,
    string ToCurrency,
    decimal? Rate,
    DateOnly? RateDate,
    string? Source,
    bool IsEstimated,
    TransactionFxQuoteStatus Status)
{
    public bool IsAvailable => Rate is > 0m;
}
