using System.Text.Json;
using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models;
using Assetra.Core.Models.Import;

namespace Assetra.Application.Import;

/// <summary>
/// 反向套用一筆 <see cref="ImportBatchHistory"/>。
/// 兩段式：(1) planning 將每筆 entry 翻成 <see cref="TradeMutation"/>，
/// 同時收集 snapshot 缺失 / 反序列化 / Id 違規等 pre-flight 失敗；
/// (2) 若 planning 全部成功，將整批 mutations 透過
/// <see cref="ITradeRepository.ApplyAtomicAsync"/> 在單一 SQLite transaction 中套用，
/// 任何資料庫錯誤都會 rollback 整批，不會留下半套用狀態。
/// 唯有套用成功後才會把 history 標為 rolled back。
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
        var mutations = new List<TradeMutation>();
        int reverted = 0, restored = 0, skipped = 0;

        foreach (var entry in record.Entries)
        {
            ct.ThrowIfCancellationRequested();

            switch (entry.Action)
            {
                case ImportBatchAction.Skipped:
                    skipped++;
                    break;

                case ImportBatchAction.Added:
                    if (entry.NewTradeId is { } addedId)
                    {
                        mutations.Add(new RemoveTradeMutation(addedId));
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
                        mutations.Add(new RemoveTradeMutation(newId));
                    }
                    if (entry.OverwrittenTradeJson is { } json)
                    {
                        Trade? snapshot;
                        try
                        {
                            snapshot = JsonSerializer.Deserialize<Trade>(json, ImportApplyService.SnapshotJsonOptions);
                        }
                        catch (JsonException jx)
                        {
                            failures.Add(new ImportRollbackFailure(entry.RowIndex,
                                $"Snapshot integrity violation: {jx.Message}"));
                            break;
                        }
                        if (snapshot is null)
                        {
                            failures.Add(new ImportRollbackFailure(entry.RowIndex, "Snapshot deserialized as null."));
                            break;
                        }
                        if (snapshot.Id == Guid.Empty)
                        {
                            failures.Add(new ImportRollbackFailure(entry.RowIndex,
                                "Snapshot integrity violation: empty Trade.Id."));
                            break;
                        }
                        mutations.Add(new AddTradeMutation(snapshot));
                        restored++;
                    }
                    else
                    {
                        failures.Add(new ImportRollbackFailure(entry.RowIndex, "Overwritten entry missing snapshot."));
                    }
                    break;
            }
        }

        if (failures.Count > 0)
        {
            return new ImportRollbackResult(historyId, 0, 0, skipped, failures);
        }

        try
        {
            await _trades.ApplyAtomicAsync(mutations, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            failures.Add(new ImportRollbackFailure(-1, $"Atomic rollback failed: {ex.Message}"));
            return new ImportRollbackResult(historyId, 0, 0, skipped, failures);
        }

        await _history.MarkRolledBackAsync(historyId, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        return new ImportRollbackResult(historyId, reverted, restored, skipped, failures);
    }
}
