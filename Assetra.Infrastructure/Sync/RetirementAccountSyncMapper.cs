using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// <see cref="RetirementAccount"/> ↔ <see cref="SyncEnvelope"/> JSON mapper。
/// Decimal 以 string 序列化，避免 double round-trip 漂移。
/// </summary>
public static class RetirementAccountSyncMapper
{
    public const string EntityType = "RetirementAccount";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SyncEnvelope ToEnvelope(RetirementAccount entity, bool isDeleted)
    {
        ArgumentNullException.ThrowIfNull(entity);
        if (isDeleted)
            return new SyncEnvelope(entity.Id, EntityType, string.Empty, entity.Version, true);

        var inv = CultureInfo.InvariantCulture;
        var dto = new RetirementAccountPayloadDto(
            entity.Id,
            entity.Name,
            entity.AccountType.ToString(),
            entity.Provider,
            entity.Balance.ToString(inv),
            entity.EmployeeContributionRate.ToString(inv),
            entity.EmployerContributionRate.ToString(inv),
            entity.YearsOfService,
            entity.LegalWithdrawalAge,
            entity.OpenedDate.ToString("yyyy-MM-dd"),
            entity.Currency,
            entity.Status.ToString(),
            entity.Notes);

        return new SyncEnvelope(entity.Id, EntityType, JsonSerializer.Serialize(dto, Options), entity.Version, false);
    }

    public static RetirementAccount FromPayload(SyncEnvelope envelope, EntityVersion version)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.EntityType != EntityType)
            throw new ArgumentException($"Expected EntityType '{EntityType}', got '{envelope.EntityType}'.", nameof(envelope));
        if (envelope.Deleted)
            throw new InvalidOperationException("Cannot decode payload of a tombstone envelope.");

        var dto = JsonSerializer.Deserialize<RetirementAccountPayloadDto>(envelope.PayloadJson, Options)
            ?? throw new InvalidOperationException("Empty RetirementAccount payload.");

        var inv = CultureInfo.InvariantCulture;
        return new RetirementAccount(
            Id: dto.Id,
            Name: dto.Name,
            AccountType: Enum.Parse<RetirementAccountType>(dto.AccountType),
            Provider: dto.Provider,
            Balance: decimal.Parse(dto.Balance, inv),
            EmployeeContributionRate: decimal.Parse(dto.EmployeeContributionRate, inv),
            EmployerContributionRate: decimal.Parse(dto.EmployerContributionRate, inv),
            YearsOfService: dto.YearsOfService,
            LegalWithdrawalAge: dto.LegalWithdrawalAge,
            OpenedDate: DateOnly.Parse(dto.OpenedDate),
            Currency: dto.Currency,
            Status: Enum.Parse<RetirementAccountStatus>(dto.Status),
            Notes: dto.Notes,
            Version: version);
    }

    private sealed record RetirementAccountPayloadDto(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("account_type")] string AccountType,
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("balance")] string Balance,
        [property: JsonPropertyName("employee_contribution_rate")] string EmployeeContributionRate,
        [property: JsonPropertyName("employer_contribution_rate")] string EmployerContributionRate,
        [property: JsonPropertyName("years_of_service")] int YearsOfService,
        [property: JsonPropertyName("legal_withdrawal_age")] int LegalWithdrawalAge,
        [property: JsonPropertyName("opened_date")] string OpenedDate,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("notes")] string? Notes);
}
