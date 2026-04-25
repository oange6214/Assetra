using Assetra.Core.Models;

namespace Assetra.Core.DomainServices;

/// <summary>
/// 對交易的 Note 套用自動分類規則。
/// 依 Priority 升冪、IsEnabled = true 的規則順序比對；第一條符合者套用 CategoryId。
/// </summary>
public static class AutoCategorizationEngine
{
    /// <summary>
    /// 根據 <paramref name="note"/> 找出第一條符合的規則並回傳其 CategoryId；找不到則回傳 null。
    /// </summary>
    public static Guid? Match(string? note, IEnumerable<AutoCategorizationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (string.IsNullOrWhiteSpace(note))
            return null;

        foreach (var rule in rules.Where(r => r.IsEnabled).OrderBy(r => r.Priority))
        {
            if (string.IsNullOrEmpty(rule.KeywordPattern))
                continue;

            var comparison = rule.MatchCaseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            if (note.Contains(rule.KeywordPattern, comparison))
                return rule.CategoryId;
        }

        return null;
    }
}
