using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Dtos;

public sealed record SellWorkflowRequest(
    Guid PortfolioEntryId,
    string Symbol,
    string Exchange,
    string Name,
    decimal BuyPrice,
    int CurrentQuantity,
    int SellQuantity,
    decimal SellPrice,
    decimal Commission,
    decimal? CommissionDiscount,
    Guid? CashAccountId,
    IReadOnlyList<Guid> EntryIdsToArchive);

public sealed record SellWorkflowResult(
    Trade Trade,
    int RemainingQuantity);
