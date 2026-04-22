namespace Assetra.Application.Portfolio.Dtos;

public sealed record PositionDeletionRequest(
    IReadOnlyCollection<Guid> EntryIds);
