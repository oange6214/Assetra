namespace Assetra.Application.Portfolio.Dtos;

public sealed record PositionMetadataUpdateRequest(
    IReadOnlyCollection<Guid> EntryIds,
    string Name,
    string Currency);

public sealed record PositionGroupUpdateRequest(
    IReadOnlyCollection<Guid> EntryIds,
    Guid PortfolioGroupId);
