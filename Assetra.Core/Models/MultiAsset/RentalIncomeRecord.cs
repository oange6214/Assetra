namespace Assetra.Core.Models.MultiAsset;

/// <summary>
/// 租金收入紀錄：記錄特定月份的租金收入與費用。
/// </summary>
public sealed record RentalIncomeRecord(
    Guid Id,
    Guid RealEstateId,
    DateOnly Month,
    decimal RentAmount,
    decimal Expenses,
    string Currency,
    string? Notes)
{
    public decimal NetIncome => RentAmount - Expenses;
}
