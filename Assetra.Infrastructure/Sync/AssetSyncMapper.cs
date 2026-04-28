using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// <see cref="AssetItem"/> ↔ <see cref="SyncEnvelope"/> 的 JSON mapper，鏡
/// <see cref="TradeSyncMapper"/>。snake_case + UnsafeRelaxedJsonEscaping（CJK 不轉義）。
/// Tombstone 的 PayloadJson 為空字串。Decimal 以 string 序列化，避免 double round-trip 漂移。
/// </summary>
public static class AssetSyncMapper
{
    public const string EntityType = "Asset";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SyncEnvelope ToEnvelope(AssetItem item, EntityVersion version, bool isDeleted)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(version);

        if (isDeleted)
        {
            return new SyncEnvelope(
                EntityId: item.Id,
                EntityType: EntityType,
                PayloadJson: string.Empty,
                Version: version,
                Deleted: true);
        }

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var dto = new AssetPayloadDto(
            item.Id,
            item.Name,
            item.Type.ToString(),
            item.GroupId,
            item.Currency,
            item.CreatedDate.ToString("yyyy-MM-dd"),
            item.IsActive,
            item.UpdatedAt?.ToUniversalTime().ToString("o"),
            item.LoanAnnualRate?.ToString(inv),
            item.LoanTermMonths,
            item.LoanStartDate?.ToString("yyyy-MM-dd"),
            item.LoanHandlingFee?.ToString(inv),
            item.LiabilitySubtype?.ToString(),
            item.BillingDay,
            item.DueDay,
            item.CreditLimit?.ToString(inv),
            item.IssuerName,
            item.Subtype);

        return new SyncEnvelope(
            EntityId: item.Id,
            EntityType: EntityType,
            PayloadJson: JsonSerializer.Serialize(dto, Options),
            Version: version,
            Deleted: false);
    }

    public static AssetItem FromPayload(SyncEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.EntityType != EntityType)
            throw new ArgumentException($"Expected EntityType '{EntityType}', got '{envelope.EntityType}'.", nameof(envelope));
        if (envelope.Deleted)
            throw new InvalidOperationException("Cannot decode payload of a tombstone envelope.");

        var dto = JsonSerializer.Deserialize<AssetPayloadDto>(envelope.PayloadJson, Options)
            ?? throw new InvalidOperationException("Empty Asset payload.");

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return new AssetItem(
            Id: dto.Id,
            Name: dto.Name,
            Type: Enum.Parse<FinancialType>(dto.Type),
            GroupId: dto.GroupId,
            Currency: dto.Currency,
            CreatedDate: DateOnly.Parse(dto.CreatedDate),
            IsActive: dto.IsActive,
            UpdatedAt: dto.UpdatedAt is null ? null : DateTime.Parse(dto.UpdatedAt, inv, System.Globalization.DateTimeStyles.RoundtripKind),
            LoanAnnualRate: dto.LoanAnnualRate is null ? null : decimal.Parse(dto.LoanAnnualRate, inv),
            LoanTermMonths: dto.LoanTermMonths,
            LoanStartDate: dto.LoanStartDate is null ? null : DateOnly.Parse(dto.LoanStartDate),
            LoanHandlingFee: dto.LoanHandlingFee is null ? null : decimal.Parse(dto.LoanHandlingFee, inv),
            LiabilitySubtype: dto.LiabilitySubtype is null ? null : Enum.Parse<LiabilitySubtype>(dto.LiabilitySubtype),
            BillingDay: dto.BillingDay,
            DueDay: dto.DueDay,
            CreditLimit: dto.CreditLimit is null ? null : decimal.Parse(dto.CreditLimit, inv),
            IssuerName: dto.IssuerName,
            Subtype: dto.Subtype);
    }

    private sealed record AssetPayloadDto(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("financial_type")] string Type,
        [property: JsonPropertyName("group_id")] Guid? GroupId,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("created_date")] string CreatedDate,
        [property: JsonPropertyName("is_active")] bool IsActive,
        [property: JsonPropertyName("updated_at")] string? UpdatedAt,
        [property: JsonPropertyName("loan_annual_rate")] string? LoanAnnualRate,
        [property: JsonPropertyName("loan_term_months")] int? LoanTermMonths,
        [property: JsonPropertyName("loan_start_date")] string? LoanStartDate,
        [property: JsonPropertyName("loan_handling_fee")] string? LoanHandlingFee,
        [property: JsonPropertyName("liability_subtype")] string? LiabilitySubtype,
        [property: JsonPropertyName("billing_day")] int? BillingDay,
        [property: JsonPropertyName("due_day")] int? DueDay,
        [property: JsonPropertyName("credit_limit")] string? CreditLimit,
        [property: JsonPropertyName("issuer_name")] string? IssuerName,
        [property: JsonPropertyName("subtype")] string? Subtype);
}
