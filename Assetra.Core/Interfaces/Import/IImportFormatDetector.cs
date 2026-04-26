using Assetra.Core.Models.Import;

namespace Assetra.Core.Interfaces.Import;

/// <summary>
/// 從檔名與內容指紋（如 CSV 標題列、Excel 第一張表的標題列）推測格式。
/// 失敗時回傳 <c>null</c>，由 UI 讓使用者手動指定。
/// </summary>
public interface IImportFormatDetector
{
    Task<ImportFormat?> DetectAsync(
        string fileName,
        Stream content,
        CancellationToken ct = default);
}
