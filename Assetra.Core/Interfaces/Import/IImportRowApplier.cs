using Assetra.Core.Models;
using Assetra.Core.Models.Import;

namespace Assetra.Core.Interfaces.Import;

/// <summary>
/// 將單一 <see cref="ImportPreviewRow"/> 套用為 <see cref="Trade"/> 並寫入資料庫。
/// 從 v0.10 起為 Reconciliation context 「Created」 resolution 提供入口，
/// 與 batch-oriented 的 <see cref="IImportApplyService"/> 互補。
/// </summary>
public interface IImportRowApplier
{
    /// <summary>
    /// 對單列做 mapping + 寫入。回傳新建立的 trade id；若 mapper 判斷該列不應被建立（例如券商列缺欄位），回 null。
    /// </summary>
    Task<Guid?> ApplyAsync(
        ImportPreviewRow row,
        ImportSourceKind sourceKind,
        ImportApplyOptions options,
        IList<string>? warnings = null,
        CancellationToken ct = default);
}
