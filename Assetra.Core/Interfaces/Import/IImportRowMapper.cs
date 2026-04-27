using Assetra.Core.Models;
using Assetra.Core.Models.Import;

namespace Assetra.Core.Interfaces.Import;

/// <summary>
/// 將 <see cref="ImportPreviewRow"/> 對應為 <see cref="Trade"/>。
/// 抽出為獨立介面，讓 v0.8 的 <c>ImportRule</c> 可以在 mapping 之前 / 之後注入分類與備註樣板。
/// </summary>
public interface IImportRowMapper
{
    /// <summary>
    /// 回傳 mapping 結果。若回傳 <see langword="null"/>，呼叫者應將該列計入 skipped 並記錄 warning。
    /// </summary>
    Trade? Map(
        ImportPreviewRow row,
        ImportSourceKind kind,
        ImportApplyOptions options,
        IList<string> warnings,
        IReadOnlyList<AutoCategorizationRule>? rules = null);
}
