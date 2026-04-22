namespace Assetra.Application.Portfolio.Dtos;

public sealed record AccountDeletionResult(
    bool Success,
    int ReferenceCount = 0);
