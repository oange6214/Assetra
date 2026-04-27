namespace Assetra.Core.Models.Import;

/// <summary>規則比對欄位來源。</summary>
public enum ImportRuleMatchField
{
    Counterparty,
    Memo,
    Either,
}

/// <summary>規則比對方式。</summary>
public enum ImportRuleMatchType
{
    Contains,
    Equals,
    StartsWith,
    Regex,
}

/// <summary>
/// 自動分類規則：當匯入列符合 <see cref="Pattern"/> 時，自動帶入 <see cref="CategoryId"/>。
/// 比對範圍由 <see cref="MatchField"/> 決定，比對方式由 <see cref="MatchType"/> 決定。
/// 啟用中的規則依 <see cref="Priority"/> 升冪掃描，第一個命中的規則勝出。
/// </summary>
public sealed record ImportRule(
    Guid Id,
    string Name,
    ImportRuleMatchField MatchField,
    ImportRuleMatchType MatchType,
    string Pattern,
    bool CaseSensitive,
    Guid CategoryId,
    int Priority,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
