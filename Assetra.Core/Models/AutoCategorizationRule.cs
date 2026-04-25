namespace Assetra.Core.Models;

/// <summary>
/// 自動分類規則：當交易 Note 包含 KeywordPattern 時，自動套用 CategoryId。
/// Priority 越小越優先；同筆交易僅套用第一條符合的規則。
/// </summary>
public sealed record AutoCategorizationRule(
    Guid Id,
    string KeywordPattern,
    Guid CategoryId,
    int Priority = 0,
    bool IsEnabled = true,
    bool MatchCaseSensitive = false);
