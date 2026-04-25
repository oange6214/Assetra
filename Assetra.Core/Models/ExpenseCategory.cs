namespace Assetra.Core.Models;

public enum CategoryKind
{
    Expense,
    Income,
}

/// <summary>
/// 收支分類。支援父子兩層分類（ParentId 指向上層分類；頂層為 null）。
/// </summary>
public sealed record ExpenseCategory(
    Guid Id,
    string Name,
    CategoryKind Kind,
    Guid? ParentId = null,
    string? Icon = null,
    string? ColorHex = null,
    int SortOrder = 0,
    bool IsArchived = false);
