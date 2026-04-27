using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;

namespace Assetra.Application.Import;

/// <summary>
/// <see cref="IImportRowApplier"/> 預設實作：透過 <see cref="IImportRowMapper"/> 對單列做 mapping，
/// 命中 <see cref="IAutoCategorizationRuleRepository"/> 規則時自動帶入分類，最後呼叫 <see cref="ITradeRepository.AddAsync"/>。
/// 用於 Reconciliation context 對「Missing」 diff 採取 Created resolution 的執行路徑。
/// </summary>
public sealed class DefaultImportRowApplier : IImportRowApplier
{
    private readonly ITradeRepository _trades;
    private readonly IImportRowMapper _mapper;
    private readonly IAutoCategorizationRuleRepository? _rules;

    public DefaultImportRowApplier(
        ITradeRepository trades,
        IImportRowMapper mapper,
        IAutoCategorizationRuleRepository? rules = null)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(mapper);
        _trades = trades;
        _mapper = mapper;
        _rules = rules;
    }

    public async Task<Guid?> ApplyAsync(
        ImportPreviewRow row,
        ImportSourceKind sourceKind,
        ImportApplyOptions options,
        IList<string>? warnings = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(options);

        var sink = warnings ?? new List<string>();

        IReadOnlyList<AutoCategorizationRule>? ruleSnapshot = null;
        if (_rules is not null)
        {
            ruleSnapshot = await _rules.GetAllAsync(ct).ConfigureAwait(false);
        }

        var trade = _mapper.Map(row, sourceKind, options, sink, ruleSnapshot);
        if (trade is null) return null;

        await _trades.AddAsync(trade).ConfigureAwait(false);
        return trade.Id;
    }
}
