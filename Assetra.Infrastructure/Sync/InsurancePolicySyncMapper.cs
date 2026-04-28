using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Models.MultiAsset;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// <see cref="InsurancePolicy"/> ↔ <see cref="SyncEnvelope"/> JSON mapper。
/// Decimal 以 string 序列化，避免 double round-trip 漂移。
/// </summary>
public static class InsurancePolicySyncMapper
{
    public const string EntityType = "InsurancePolicy";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SyncEnvelope ToEnvelope(InsurancePolicy policy, bool isDeleted)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (isDeleted)
            return new SyncEnvelope(policy.Id, EntityType, string.Empty, policy.Version, true);

        var inv = CultureInfo.InvariantCulture;
        var dto = new InsurancePolicyPayloadDto(
            policy.Id,
            policy.Name,
            policy.PolicyNumber,
            policy.Type.ToString(),
            policy.Insurer,
            policy.StartDate.ToString("yyyy-MM-dd"),
            policy.MaturityDate?.ToString("yyyy-MM-dd"),
            policy.FaceValue.ToString(inv),
            policy.CurrentCashValue.ToString(inv),
            policy.AnnualPremium.ToString(inv),
            policy.Currency,
            policy.Status.ToString(),
            policy.Notes);

        return new SyncEnvelope(policy.Id, EntityType, JsonSerializer.Serialize(dto, Options), policy.Version, false);
    }

    public static InsurancePolicy FromPayload(SyncEnvelope envelope, EntityVersion version)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.EntityType != EntityType)
            throw new ArgumentException($"Expected EntityType '{EntityType}', got '{envelope.EntityType}'.", nameof(envelope));
        if (envelope.Deleted)
            throw new InvalidOperationException("Cannot decode payload of a tombstone envelope.");

        var dto = JsonSerializer.Deserialize<InsurancePolicyPayloadDto>(envelope.PayloadJson, Options)
            ?? throw new InvalidOperationException("Empty InsurancePolicy payload.");

        var inv = CultureInfo.InvariantCulture;
        return new InsurancePolicy(
            Id: dto.Id,
            Name: dto.Name,
            PolicyNumber: dto.PolicyNumber,
            Type: Enum.Parse<InsuranceType>(dto.Type),
            Insurer: dto.Insurer,
            StartDate: DateOnly.Parse(dto.StartDate),
            MaturityDate: dto.MaturityDate is null ? null : DateOnly.Parse(dto.MaturityDate),
            FaceValue: decimal.Parse(dto.FaceValue, inv),
            CurrentCashValue: decimal.Parse(dto.CurrentCashValue, inv),
            AnnualPremium: decimal.Parse(dto.AnnualPremium, inv),
            Currency: dto.Currency,
            Status: Enum.Parse<InsurancePolicyStatus>(dto.Status),
            Notes: dto.Notes,
            Version: version);
    }

    private sealed record InsurancePolicyPayloadDto(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("policy_number")] string PolicyNumber,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("insurer")] string Insurer,
        [property: JsonPropertyName("start_date")] string StartDate,
        [property: JsonPropertyName("maturity_date")] string? MaturityDate,
        [property: JsonPropertyName("face_value")] string FaceValue,
        [property: JsonPropertyName("current_cash_value")] string CurrentCashValue,
        [property: JsonPropertyName("annual_premium")] string AnnualPremium,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("notes")] string? Notes);
}
