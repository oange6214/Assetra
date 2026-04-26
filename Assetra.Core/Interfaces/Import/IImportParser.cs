using Assetra.Core.Models.Import;

namespace Assetra.Core.Interfaces.Import;

/// <summary>
/// 將檔案串流解析成 <see cref="ImportPreviewRow"/> 清單。
/// 一個 <see cref="ImportFormat"/> 對應一個實作。
/// </summary>
public interface IImportParser
{
    ImportFormat Format { get; }
    ImportFileType FileType { get; }

    Task<IReadOnlyList<ImportPreviewRow>> ParseAsync(
        Stream content,
        CancellationToken ct = default);
}
