using Assetra.Core.Models.Import;

namespace Assetra.Core.Interfaces.Import;

/// <summary>
/// 將 <see cref="ImportBatchHistory"/> 中記錄的 entries 反向操作，
/// 已新增的 trade 刪除、被覆蓋的 trade 還原。
/// </summary>
public interface IImportRollbackService
{
    Task<ImportRollbackResult> RollbackAsync(Guid historyId, CancellationToken ct = default);
}
