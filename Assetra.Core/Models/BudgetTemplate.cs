namespace Assetra.Core.Models;

/// <summary>
/// 預算範本：可套用至新月份產生 Budget 列表。
/// </summary>
public sealed record BudgetTemplate(
    Guid Id,
    string Name,
    BudgetMode Mode,
    string Currency = "TWD",
    string? Note = null);

/// <summary>
/// 範本內單筆預算項目。
/// </summary>
public sealed record BudgetTemplateItem(
    Guid Id,
    Guid TemplateId,
    Guid? CategoryId,
    decimal Amount);
