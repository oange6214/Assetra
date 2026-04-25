namespace Assetra.Core.Models;

public enum BudgetMode
{
    Monthly,
    Yearly,
}

/// <summary>
/// 預算設定：對特定分類於指定週期（月/年）的預算上限。
/// CategoryId 為 null 表示總預算。
/// </summary>
public sealed record Budget(
    Guid Id,
    Guid? CategoryId,
    BudgetMode Mode,
    int Year,
    int? Month,
    decimal Amount,
    string Currency = "TWD",
    string? Note = null);
