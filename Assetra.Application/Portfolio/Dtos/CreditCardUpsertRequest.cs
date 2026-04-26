using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Dtos;

public sealed record CreateCreditCardRequest(
    string Name,
    string Currency,
    DateOnly CreatedDate,
    int? BillingDay,
    int? DueDay,
    decimal? CreditLimit,
    string? IssuerName,
    string? Subtype = null);

public sealed record UpdateCreditCardRequest(
    Guid CardId,
    string Name,
    string Currency,
    DateOnly CreatedDate,
    int? BillingDay,
    int? DueDay,
    decimal? CreditLimit,
    string? IssuerName,
    string? Subtype = null);

public sealed record CreditCardUpsertResult(
    AssetItem CreditCard);
