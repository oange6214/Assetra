namespace Assetra.Core.Models.MultiAsset;

/// <summary>
/// 保費繳納紀錄：記錄每次繳納的保費金額與日期。
/// </summary>
public sealed record InsurancePremiumRecord(
    Guid Id,
    Guid PolicyId,
    DateOnly PaidDate,
    decimal Amount,
    string Currency,
    string? Notes);
