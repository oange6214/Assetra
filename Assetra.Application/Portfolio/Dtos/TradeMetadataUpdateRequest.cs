namespace Assetra.AppLayer.Portfolio.Dtos;

public sealed record TradeMetadataUpdateRequest(
    Guid TradeId,
    DateTime TradeDate,
    string? Note);
