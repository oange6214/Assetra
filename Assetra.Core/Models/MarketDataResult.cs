namespace Assetra.Core.Models;

public sealed record MarketDataResult<T>
    where T : notnull
{
    private MarketDataResult(T? value, MarketDataError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }
    public MarketDataError? Error { get; }
    public bool IsSuccess => Error is null;

    public static MarketDataResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(value, null);
    }

    public static MarketDataResult<T> Failure(MarketDataError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(default, error);
    }
}
