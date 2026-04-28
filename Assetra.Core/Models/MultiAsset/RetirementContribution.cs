namespace Assetra.Core.Models.MultiAsset;

/// <summary>
/// 退休專戶提撥紀錄：記錄某年度勞工/雇主提撥金額。
/// </summary>
public sealed record RetirementContribution(
    Guid Id,
    Guid AccountId,
    int Year,
    decimal EmployeeAmount,
    decimal EmployerAmount,
    string Currency,
    string? Notes)
{
    public decimal TotalAmount => EmployeeAmount + EmployerAmount;
}
