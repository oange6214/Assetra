using System.Text.Json;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;

namespace Assetra.Application.Import;

/// <summary>
/// 反向套用一筆 <see cref="ImportBatchHistory"/>。
/// 由於 <see cref="ITradeRepository"/> 沒有跨呼叫的交易範圍，
/// 採取「逐列嘗試 + 收集失敗」策略；全部成功才標記 history 為 rolled back。
/// </summary>
public sealed class ImportRollbackService : IImportRollbackService
{
    private readonly ITradeRepository _trades;
    private readonly IImportBatchHistoryRepository _history;

    public ImportRollbackService(ITradeRepository trades, IImportBatchHistoryRepository history)
    {
        _trades = trades ?? throw new ArgumentNullException(nameof(trades));
        _history = history ?? throw new ArgumentNullException(nameof(history));
    }

    public async Task<ImportRollbackResult> RollbackAsync(Guid historyId, CancellationToken ct = default)
    {
        var record = await _history.GetByIdAsync(historyId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Import history {historyId} not found.");

        if (record.IsRolledBack)
        {
            return new ImportRollbackResult(historyId, 0, 0, 0,
                new[] { new ImportRollbackFailure(-1, "History already rolled back.") });
        }

        var failures = new List<ImportRollbackFailure>();
        int reverted = 0, restored = 0, skipped = 0;

        foreach (var entry in record.Entries)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                switch (entry.Action)
                {
                    case ImportBatchAction.Skipped:
                        skipped++;
                        break;

                    case ImportBatchAction.Added:
                        if (entry.NewTradeId is { } addedId)
                        {
                            await _trades.RemoveAsync(addedId).ConfigureAwait(false);
                            reverted++;
                        }
                        else
                        {
                            failures.Add(new ImportRollbackFailure(entry.RowIndex, "Added entry missing NewTradeId."));
                        }
                        break;

                    case ImportBatchAction.Overwritten:
                        if (entry.NewTradeId is { } newId)
                        {
                            await _trades.RemoveAsync(newId).ConfigureAwait(false);
                        }
                        if (entry.OverwrittenTradeJson is { } json)
                        {
                            var snapshot = JsonSerializer.Deserialize<Trade>(json, ImportApplyService.SnapshotJsonOptions);
                            if (snapshot is null)
                            {
                                failures.Add(new ImportRollbackFailure(entry.RowIndex, "Snapshot deserialized as null."));
                                break;
                            }
                            await _trades.AddAsync(snapshot).ConfigureAwait(false);
                            restored++;
                        }
                        else
                        {
                            failures.Add(new ImportRollbackFailure(entry.RowIndex, "Overwritten entry missing snapshot."));
                        }
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(new ImportRollbackFailure(entry.RowIndex, ex.Message));
            }
        }

        if (failures.Count == 0)
        {
            await _history.MarkRolledBackAsync(historyId, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        }

        return new ImportRollbackResult(historyId, reverted, restored, skipped, failures);
    }
}
