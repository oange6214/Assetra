using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Dtos;

public sealed record TradeDeletionRequest(
    Guid TradeId,
    TradeType TradeType,
    string Symbol,
    int Quantity,
    Guid? PortfolioEntryId);

public sealed record TradeDeletionResult(
    bool Success,
    bool BlockedBySell = false);
