using Assetra.Core.Models.Import;

namespace Assetra.Core.Interfaces.Import;

/// <summary>
/// OCR adapter：將 PDF 圖片頁的 raw 影像位元組識別為文字。
/// <para>v0.19.0 僅定義介面；具體實作（Tesseract.NET wiring）留待 v0.19.1+。</para>
/// </summary>
public interface IOcrAdapter
{
    Task<OcrResult> RecognizeAsync(
        ReadOnlyMemory<byte> imageBytes,
        CancellationToken ct = default);
}
