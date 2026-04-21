namespace Assetra.AppLayer.Portfolio.Dtos;

public sealed record AccountDeletionResult(
    bool Success,
    int ReferenceCount = 0);
