namespace Assetra.AppLayer.Portfolio.Dtos;

public sealed record PositionDeletionRequest(
    IReadOnlyCollection<Guid> EntryIds);
