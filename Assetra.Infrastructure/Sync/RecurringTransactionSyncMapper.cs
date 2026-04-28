using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// <see cref="RecurringTransaction"/> ↔ <see cref="SyncEnvelope"/> mapper（v0.20.11）。
/// EntityType = "RecurringTransaction". Decimal 以 invariant string 避免漂移；DateTime 用 ISO-8601 round-trip。
/// </summary>
public static class RecurringTransactionSyncMapper
{
    public const string EntityType = "RecurringTransaction";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SyncEnvelope ToEnvelope(RecurringTransaction rt, EntityVersion version, bool isDeleted)
    {
        ArgumentNullException.ThrowIfNull(rt);
        ArgumentNullException.ThrowIfNull(version);

        if (isDeleted)
        {
            return new SyncEnvelope(rt.Id, EntityType, string.Empty, version, Deleted: true);
        }

        var inv = CultureInfo.InvariantCulture;
        var dto = new RecurringPayloadDto(
            rt.Id,
            rt.Name,
            (int)rt.TradeType,
            rt.Amount.ToString(inv),
            rt.CashAccountId,
            rt.CategoryId,
            (int)rt.Frequency,
            rt.Interval,
            rt.StartDate.ToString("o", inv),
            rt.EndDate?.ToString("o", inv),
            (int)rt.GenerationMode,
            rt.LastGeneratedAt?.ToString("o", inv),
            rt.NextDueAt?.ToString("o", inv),
            rt.Note,
            rt.IsEnabled);

        return new SyncEnvelope(
            rt.Id, EntityType,
            JsonSerializer.Serialize(dto, Options),
            version, Deleted: false);
    }

    public static RecurringTransaction FromPayload(SyncEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.EntityType != EntityType)
            throw new ArgumentException($"Expected EntityType '{EntityType}', got '{envelope.EntityType}'.", nameof(envelope));
        if (envelope.Deleted)
            throw new InvalidOperationException("Cannot decode payload of a tombstone envelope.");

        var dto = JsonSerializer.Deserialize<RecurringPayloadDto>(envelope.PayloadJson, Options)
            ?? throw new InvalidOperationException("Empty RecurringTransaction payload.");

        var inv = CultureInfo.InvariantCulture;
        return new RecurringTransaction(
            Id: dto.Id,
            Name: dto.Name,
            TradeType: (TradeType)dto.TradeType,
            Amount: decimal.Parse(dto.Amount, inv),
            CashAccountId: dto.CashAccountId,
            CategoryId: dto.CategoryId,
            Frequency: (RecurrenceFrequency)dto.Frequency,
            Interval: dto.Interval,
            StartDate: DateTime.Parse(dto.StartDate, inv, DateTimeStyles.RoundtripKind),
            EndDate: dto.EndDate is null ? null : DateTime.Parse(dto.EndDate, inv, DateTimeStyles.RoundtripKind),
            GenerationMode: (AutoGenerationMode)dto.GenerationMode,
            LastGeneratedAt: dto.LastGeneratedAt is null ? null : DateTime.Parse(dto.LastGeneratedAt, inv, DateTimeStyles.RoundtripKind),
            NextDueAt: dto.NextDueAt is null ? null : DateTime.Parse(dto.NextDueAt, inv, DateTimeStyles.RoundtripKind),
            Note: dto.Note,
            IsEnabled: dto.IsEnabled);
    }

    private sealed record RecurringPayloadDto(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("trade_type")] int TradeType,
        [property: JsonPropertyName("amount")] string Amount,
        [property: JsonPropertyName("cash_account_id")] Guid? CashAccountId,
        [property: JsonPropertyName("category_id")] Guid? CategoryId,
        [property: JsonPropertyName("frequency")] int Frequency,
        [property: JsonPropertyName("interval_value")] int Interval,
        [property: JsonPropertyName("start_date")] string StartDate,
        [property: JsonPropertyName("end_date")] string? EndDate,
        [property: JsonPropertyName("generation_mode")] int GenerationMode,
        [property: JsonPropertyName("last_generated_at")] string? LastGeneratedAt,
        [property: JsonPropertyName("next_due_at")] string? NextDueAt,
        [property: JsonPropertyName("note")] string? Note,
        [property: JsonPropertyName("is_enabled")] bool IsEnabled);
}
