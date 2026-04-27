using System.Text.RegularExpressions;
using Assetra.Core.Models;

namespace Assetra.Core.DomainServices;

/// <summary>
/// 對交易內容套用 <see cref="AutoCategorizationRule"/>。<br/>
/// 啟用中、按 <c>Priority</c> 升冪掃描，第一條命中即套用其 <c>CategoryId</c>。<br/>
/// Regex 編譯失敗的規則會被略過，避免單一壞規則拖垮整批處理。<br/>
/// 規則 <see cref="AutoCategorizationRule.AppliesTo"/> 不包含當前 <see cref="AutoCategorizationContext.Source"/> 的會被略過。
/// </summary>
public static class AutoCategorizationEngine
{
    /// <summary>
    /// 手動模式入口：對 <paramref name="note"/> 做比對（與 v0.7 行為相容）。<br/>
    /// 內部包成 <see cref="AutoCategorizationContext"/> 並標記 Source = Manual。
    /// </summary>
    public static Guid? Match(string? note, IEnumerable<AutoCategorizationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return Match(
            new AutoCategorizationContext(
                Note: note,
                Counterparty: null,
                Memo: null,
                Source: AutoCategorizationScope.Manual),
            rules);
    }

    /// <summary>新 API：依 <paramref name="context"/> 套用規則並回傳第一條命中的 CategoryId。</summary>
    public static Guid? Match(AutoCategorizationContext context, IEnumerable<AutoCategorizationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rules);

        foreach (var rule in rules.Where(r => r.IsEnabled).OrderBy(r => r.Priority))
        {
            if (string.IsNullOrEmpty(rule.KeywordPattern)) continue;
            if ((rule.AppliesTo & context.Source) == 0) continue;
            if (TryMatch(rule, context)) return rule.CategoryId;
        }

        return null;
    }

    private static bool TryMatch(AutoCategorizationRule rule, AutoCategorizationContext ctx)
    {
        return rule.MatchField switch
        {
            AutoCategorizationMatchField.Counterparty => MatchOne(rule, ctx.Counterparty),
            AutoCategorizationMatchField.Memo => MatchOne(rule, ctx.Memo),
            AutoCategorizationMatchField.Either =>
                MatchOne(rule, ctx.Counterparty) || MatchOne(rule, ctx.Memo),
            AutoCategorizationMatchField.AnyText => MatchOne(rule, ResolveAnyText(ctx)),
            _ => false,
        };
    }

    private static string? ResolveAnyText(AutoCategorizationContext ctx)
    {
        if (!string.IsNullOrEmpty(ctx.Note)) return ctx.Note;
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(ctx.Counterparty)) parts.Add(ctx.Counterparty!);
        if (!string.IsNullOrWhiteSpace(ctx.Memo)) parts.Add(ctx.Memo!);
        return parts.Count == 0 ? null : string.Join(" / ", parts);
    }

    private static bool MatchOne(AutoCategorizationRule rule, string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var comparison = rule.MatchCaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
        return rule.MatchType switch
        {
            AutoCategorizationMatchType.Contains => value.Contains(rule.KeywordPattern, comparison),
            AutoCategorizationMatchType.Equals => value.Equals(rule.KeywordPattern, comparison),
            AutoCategorizationMatchType.StartsWith => value.StartsWith(rule.KeywordPattern, comparison),
            AutoCategorizationMatchType.Regex => TryRegex(rule, value),
            _ => false,
        };
    }

    private static bool TryRegex(AutoCategorizationRule rule, string value)
    {
        try
        {
            var options = RegexOptions.CultureInvariant;
            if (!rule.MatchCaseSensitive) options |= RegexOptions.IgnoreCase;
            return Regex.IsMatch(value, rule.KeywordPattern, options, TimeSpan.FromMilliseconds(200));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
