using Assetra.Core.Models;

namespace Assetra.AppLayer.Portfolio.Dtos;

public sealed record CreateAccountRequest(
    string Name,
    string Currency,
    DateOnly CreatedDate);

public sealed record UpdateAccountRequest(
    Guid AccountId,
    string Name,
    string Currency,
    DateOnly CreatedDate);

public sealed record AccountUpsertResult(
    AssetItem Account);
