namespace Assetra.Core.Models.Reconciliation;

/// <summary>
/// 一次對帳作業的標頭。
/// <para>
/// <see cref="AccountId"/> 為對帳基準帳戶（CashAccount 或 BrokerAccount 等）。
/// <see cref="SourceBatchId"/> 為若由 ImportBatchHistory 衍生時的來源 batchId；手動上傳則為 null。
/// </para>
/// </summary>
public sealed record ReconciliationSession(
    Guid Id,
    Guid AccountId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    Guid? SourceBatchId,
    DateTimeOffset CreatedAt,
    ReconciliationStatus Status,
    string? Note);
