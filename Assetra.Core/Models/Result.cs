namespace Assetra.Core.Models;

public record Result<T>
{
    public T? Value { get; init; }
    public string? Error { get; init; }
    public bool IsSuccess { get; init; }

    private Result() { }

    public static Result<T> Success(T value) =>
        new() { Value = value, IsSuccess = true };

    public static Result<T> Failure(string error) =>
        new() { Error = error, IsSuccess = false };
}
