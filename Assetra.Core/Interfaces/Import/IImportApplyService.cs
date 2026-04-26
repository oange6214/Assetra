using Assetra.Core.Models.Import;

namespace Assetra.Core.Interfaces.Import;

public interface IImportApplyService
{
    Task<ImportApplyResult> ApplyAsync(
        ImportBatch batch,
        ImportApplyOptions options,
        CancellationToken ct = default);
}
