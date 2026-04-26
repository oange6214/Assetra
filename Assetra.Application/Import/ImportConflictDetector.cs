using Assetra.Core.Interfaces;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models.Import;

namespace Assetra.Application.Import;

public sealed class ImportConflictDetector : IImportConflictDetector
{
    private readonly ITradeRepository _trades;

    public ImportConflictDetector(ITradeRepository trades)
    {
        _trades = trades ?? throw new ArgumentNullException(nameof(trades));
    }

    public async Task<ImportBatch> DetectAsync(ImportBatch batch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(batch);

        var existing = await _trades.GetAllAsync().ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var index = existing.GroupBy(ImportMatchKey.FromTrade)
            .ToDictionary(g => g.Key, g => g.First().Id);

        var conflicts = new List<ImportConflict>();
        foreach (var row in batch.Rows)
        {
            var key = ImportMatchKey.FromPreview(row);
            if (index.TryGetValue(key, out var existingId))
            {
                conflicts.Add(new ImportConflict(row, existingId, null));
            }
        }

        return batch with
        {
            Conflicts = conflicts,
            Status = ImportBatchStatus.Previewing,
        };
    }
}
