using System.Text.RegularExpressions;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models.Import;

namespace Assetra.Application.Import;

/// <summary>
/// 預設規則引擎：在啟動時或顯式呼叫 <see cref="RefreshAsync"/> 時，
/// 從 <see cref="IImportRuleRepository"/> 取得啟用中的規則並依 <c>Priority</c> 升冪排序。
/// 比對是 thread-safe 的（採取 immutable snapshot），故可重複叫 <see cref="TryResolveCategory"/>。
/// Regex 編譯失敗的規則會被忽略，避免單一壞規則拖垮整批匯入。
/// </summary>
public sealed class ImportRuleEngine : IImportRuleEngine
{
    private readonly IImportRuleRepository _repository;
    private IReadOnlyList<CompiledRule> _snapshot = Array.Empty<CompiledRule>();

    public ImportRuleEngine(IImportRuleRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var rules = await _repository.GetAllAsync(ct).ConfigureAwait(false);
        _snapshot = rules
            .Where(r => r.IsEnabled && !string.IsNullOrEmpty(r.Pattern))
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.CreatedAt)
            .Select(Compile)
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();
    }

    public bool TryResolveCategory(ImportPreviewRow row, out Guid? categoryId)
    {
        ArgumentNullException.ThrowIfNull(row);
        var snapshot = _snapshot;
        foreach (var rule in snapshot)
        {
            if (Matches(rule, row))
            {
                categoryId = rule.CategoryId;
                return true;
            }
        }

        categoryId = null;
        return false;
    }

    private static bool Matches(CompiledRule rule, ImportPreviewRow row)
    {
        return rule.Source.MatchField switch
        {
            ImportRuleMatchField.Counterparty => MatchOne(rule, row.Counterparty),
            ImportRuleMatchField.Memo => MatchOne(rule, row.Memo),
            ImportRuleMatchField.Either => MatchOne(rule, row.Counterparty) || MatchOne(rule, row.Memo),
            _ => false,
        };
    }

    private static bool MatchOne(CompiledRule rule, string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        var src = rule.Source;
        var comparison = src.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return src.MatchType switch
        {
            ImportRuleMatchType.Contains => value.Contains(src.Pattern, comparison),
            ImportRuleMatchType.Equals => value.Equals(src.Pattern, comparison),
            ImportRuleMatchType.StartsWith => value.StartsWith(src.Pattern, comparison),
            ImportRuleMatchType.Regex => rule.Regex is not null && rule.Regex.IsMatch(value),
            _ => false,
        };
    }

    private static CompiledRule? Compile(ImportRule rule)
    {
        if (rule.MatchType != ImportRuleMatchType.Regex)
            return new CompiledRule(rule, null);

        try
        {
            var options = RegexOptions.CultureInvariant | RegexOptions.Compiled;
            if (!rule.CaseSensitive) options |= RegexOptions.IgnoreCase;
            return new CompiledRule(rule, new Regex(rule.Pattern, options, TimeSpan.FromMilliseconds(200)));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private sealed record CompiledRule(ImportRule Source, Regex? Regex)
    {
        public Guid CategoryId => Source.CategoryId;
    }
}
