using Assetra.Core.Models.Import;

namespace Assetra.Core.Interfaces.Import;

/// <summary>
/// 對照資料庫中既有交易，標記匯入批次中已存在的列。
/// 不修改原批次，回傳一個新的 <see cref="ImportBatch"/>（已填入 <c>Conflicts</c>）。
/// </summary>
public interface IImportConflictDetector
{
    Task<ImportBatch> DetectAsync(ImportBatch batch, CancellationToken ct = default);
}
