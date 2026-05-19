using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Assetra.Core.Models;
using Assetra.Core.Models.Sync;

namespace Assetra.Infrastructure.Sync;

/// <summary>
/// <see cref="FinancialGoal"/> ↔ <see cref="SyncEnvelope"/> JSON mapper.
/// Mirrors <see cref="CategorySyncMapper"/>. Decimals serialized as
/// invariant-culture strings to avoid round-trip drift.
/// </summary>
public static class FinancialGoalSyncMapper
{
    public const string EntityType = "FinancialGoal";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static SyncEnvelope ToEnvelope(FinancialGoal goal, EntityVersion version, bool isDeleted)
    {
        ArgumentNullException.ThrowIfNull(goal);
        ArgumentNullException.ThrowIfNull(version);

        if (isDeleted)
        {
            return new SyncEnvelope(
                EntityId: goal.Id,
                EntityType: EntityType,
                PayloadJson: string.Empty,
                Version: version,
                Deleted: true);
        }

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var dto = new GoalPayloadDto(
            goal.Id,
            goal.Name,
            goal.TargetAmount.ToString(inv),
            goal.CurrentAmount.ToString(inv),
            goal.Deadline?.ToString("yyyy-MM-dd"),
            goal.Notes,
            goal.LinkedAssetClass,
            goal.PortfolioGroupId);

        return new SyncEnvelope(
            EntityId: goal.Id,
            EntityType: EntityType,
            PayloadJson: JsonSerializer.Serialize(dto, Options),
            Version: version,
            Deleted: false);
    }

    public static FinancialGoal FromPayload(SyncEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.EntityType != EntityType)
            throw new ArgumentException($"Expected EntityType '{EntityType}', got '{envelope.EntityType}'.", nameof(envelope));
        if (envelope.Deleted)
            throw new InvalidOperationException("Cannot decode payload of a tombstone envelope.");

        var dto = JsonSerializer.Deserialize<GoalPayloadDto>(envelope.PayloadJson, Options)
            ?? throw new InvalidOperationException("Empty FinancialGoal payload.");

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        DateOnly? deadline = string.IsNullOrWhiteSpace(dto.Deadline)
            ? null
            : DateOnly.Parse(dto.Deadline);
        return new FinancialGoal(
            Id: dto.Id,
            Name: dto.Name,
            TargetAmount: decimal.Parse(dto.TargetAmount, inv),
            CurrentAmount: decimal.Parse(dto.CurrentAmount, inv),
            Deadline: deadline,
            Notes: dto.Notes,
            LinkedAssetClass: dto.LinkedAssetClass,
            PortfolioGroupId: dto.PortfolioGroupId);
    }

    private sealed record GoalPayloadDto(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("target_amount")] string TargetAmount,
        [property: JsonPropertyName("current_amount")] string CurrentAmount,
        [property: JsonPropertyName("deadline")] string? Deadline,
        [property: JsonPropertyName("notes")] string? Notes,
        [property: JsonPropertyName("linked_asset_class")] string? LinkedAssetClass,
        [property: JsonPropertyName("portfolio_group_id")] Guid? PortfolioGroupId);
}
