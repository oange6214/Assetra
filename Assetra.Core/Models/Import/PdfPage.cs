namespace Assetra.Core.Models.Import;

/// <summary>
/// PDF 抽取後的單頁中介結構。
/// <para>由 <see cref="Interfaces.Import.IPdfStatementParser"/> 產出，餵給 <c>PdfRowExtractor</c> 做行級正規化。</para>
/// <para><see cref="Source"/> 區分純文字模式（<see cref="PdfPageSource.Text"/>）與 OCR 圖片模式（<see cref="PdfPageSource.Ocr"/>）；
/// OCR 來源時 <see cref="OcrConfidence"/> 為 0~1 的平均信心分數，由 <see cref="Interfaces.Import.IOcrAdapter"/> 提供。</para>
/// </summary>
public sealed record PdfPage(
    int PageIndex,
    string Text,
    PdfPageSource Source,
    double? OcrConfidence = null,
    byte[]? ImageBytes = null);

public enum PdfPageSource
{
    /// <summary>PDF 內嵌文字（PdfPig text extraction），高保真。</summary>
    Text = 0,
    /// <summary>掃描圖片頁，經 OCR 識別後轉文字，可能含錯誤。</summary>
    Ocr = 1,
}
