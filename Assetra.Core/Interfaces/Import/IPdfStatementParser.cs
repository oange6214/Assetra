using Assetra.Core.Models.Import;

namespace Assetra.Core.Interfaces.Import;

/// <summary>
/// 從 PDF 串流抽取頁面文字。文字模式 PDF 直接 extract；圖片模式 PDF 委派給 <see cref="IOcrAdapter"/>。
/// <para>v0.19.0 僅定義介面；具體實作（PdfPig + Tesseract wiring）留待 v0.19.1+。</para>
/// </summary>
public interface IPdfStatementParser
{
    Task<IReadOnlyList<PdfPage>> ExtractPagesAsync(
        Stream content,
        CancellationToken ct = default);
}
