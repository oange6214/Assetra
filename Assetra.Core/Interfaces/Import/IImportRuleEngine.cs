using Assetra.Core.Models.Import;

namespace Assetra.Core.Interfaces.Import;

/// <summary>
/// 套用 <see cref="ImportRule"/> 集合，針對單筆 <see cref="ImportPreviewRow"/> 嘗試解析分類。
/// 實作可在啟動時 cache 規則列表；呼叫者若想強制重新整理，應呼叫 <see cref="RefreshAsync"/>。
/// </summary>
public interface IImportRuleEngine
{
    /// <summary>
    /// 嘗試為 <paramref name="row"/> 找出符合的分類 Id。
    /// 命中回傳 true 並輸出 categoryId；未命中回傳 false 並輸出 null。
    /// </summary>
    bool TryResolveCategory(ImportPreviewRow row, out Guid? categoryId);

    /// <summary>從儲存層重新載入規則快照。</summary>
    Task RefreshAsync(CancellationToken ct = default);
}
