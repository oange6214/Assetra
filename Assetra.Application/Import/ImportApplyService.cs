using System.Text.Json;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;
using Assetra.Core.DomainServices;

namespace Assetra.Application.Import;

/// <summary>
/// 將通過預覽的 <see cref="ImportBatch"/> 轉換為 <see cref="Trade"/> 並寫入資料庫。
/// 衝突列依各自的 <see cref="ImportConflictResolution"/> 處理：
/// Skip 跳過、AddAnyway 仍建立、Overwrite 視為以新覆蓋舊（先讀 snapshot → 刪 → 建）。
/// 套用結束後若有注入 <see cref="IImportBatchHistoryRepository"/>，會留下 batch history 供 rollback。
/// </summary>
public sealed class ImportApplyService : IImportApplyService
{
    public static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = null,
        MaxDepth = 16,
    };

    private readonly ITradeRepository _trades;
    private readonly IImportRowMapper _mapper;
    private readonly IImportBatchHistoryRepository? _history;
    private readonly IAutoCategorizationRuleRepository? _rules;

    public ImportApplyService(
        ITradeRepository trades,
        IImportRowMapper mapper,
        IImportBatchHistoryRepository? history = null,
        IAutoCategorizationRuleRepository? rules = null)
    {
        _trades = trades ?? throw new ArgumentNullException(nameof(trades));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _history = history;
        _rules = rules;
    }

    public async Task<ImportApplyResult> ApplyAsync(
        ImportBatch batch,
        ImportApplyOptions options,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(options);

        var conflictByRowIndex = batch.Conflicts.ToDictionary(c => c.Row.RowIndex);
        var warnings = new List<string>();
        var entries = new List<ImportBatchEntry>();

        IReadOnlyList<AutoCategorizationRule>? ruleSnapshot = null;
        if (_rules is not null)
        {
            ruleSnapshot = await _rules.GetAllAsync(ct).ConfigureAwait(false);
        }

        int applied = 0, skipped = 0, overwritten = 0;

        foreach (var row in batch.Rows)
        {
            ct.ThrowIfCancellationRequested();

            var hasConflict = conflictByRowIndex.TryGetValue(row.RowIndex, out var conflict);
            var resolution = hasConflict ? conflict!.Resolution : ImportConflictResolution.AddAnyway;

            if (hasConflict && resolution == ImportConflictResolution.Skip)
            {
                skipped++;
                entries.Add(new ImportBatchEntry(
                    row.RowIndex, ImportBatchAction.Skipped, null, null,
                    PreviewRowJson: JsonSerializer.Serialize(row, SnapshotJsonOptions)));
                continue;
            }

            string? overwrittenJson = null;
            if (resolution == ImportConflictResolution.Overwrite
                && conflict?.ExistingTradeId is { } existingId)
            {
                var existing = await _trades.GetByIdAsync(existingId, ct).ConfigureAwait(false);
                if (existing is not null)
                {
                    overwrittenJson = JsonSerializer.Serialize(existing, SnapshotJsonOptions);
                }
                await _trades.RemoveAsync(existingId, ct).ConfigureAwait(false);
                overwritten++;
            }

            var trade = _mapper.Map(row, batch.SourceKind, options, warnings, ruleSnapshot);
            if (trade is null)
            {
                skipped++;
                entries.Add(new ImportBatchEntry(
                    row.RowIndex, ImportBatchAction.Skipped, null, null,
                    PreviewRowJson: JsonSerializer.Serialize(row, SnapshotJsonOptions)));
                continue;
            }

            await _trades.AddAsync(trade, ct).ConfigureAwait(false);
            applied++;

            entries.Add(new ImportBatchEntry(
                RowIndex: row.RowIndex,
                Action: overwrittenJson is not null ? ImportBatchAction.Overwritten : ImportBatchAction.Added,
                NewTradeId: trade.Id,
                OverwrittenTradeJson: overwrittenJson,
                PreviewRowJson: JsonSerializer.Serialize(row, SnapshotJsonOptions)));
        }

        Guid? historyId = null;
        if (_history is not null)
        {
            historyId = Guid.NewGuid();
            var history = new ImportBatchHistory(
                Id: historyId.Value,
                BatchId: batch.Id,
                FileName: batch.FileName,
                Format: batch.Format,
                AppliedAt: DateTimeOffset.UtcNow,
                RowsApplied: applied,
                RowsSkipped: skipped,
                RowsOverwritten: overwritten,
                IsRolledBack: false,
                RolledBackAt: null,
                Entries: entries);
            await _history.SaveAsync(history, ct).ConfigureAwait(false);
        }

        return new ImportApplyResult(
            RowsConsidered: batch.RowCount,
            RowsApplied: applied,
            RowsSkipped: skipped,
            RowsOverwritten: overwritten,
            Warnings: warnings,
            HistoryId: historyId);
    }
}
