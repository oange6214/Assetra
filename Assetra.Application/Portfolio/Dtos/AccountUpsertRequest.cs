using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Dtos;

public sealed record CreateAccountRequest(
    string Name,
    string Currency,
    DateOnly CreatedDate,
    string? Subtype = null);

public sealed record UpdateAccountRequest(
    Guid AccountId,
    string Name,
    string Currency,
    DateOnly CreatedDate,
    string? Subtype = null);

public sealed record AccountUpsertResult(
    AssetItem Account);
