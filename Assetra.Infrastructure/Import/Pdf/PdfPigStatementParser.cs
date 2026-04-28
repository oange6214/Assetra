using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models.Import;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Assetra.Infrastructure.Import.Pdf;

/// <summary>
/// PdfPig 實作的 <see cref="IPdfStatementParser"/>，文字模式 PDF 直接抽取。
/// <para>判斷規則：呼叫 <see cref="ContentOrderTextExtractor"/> 取得排序後的頁面文字；
/// 若文字非空白則視為 <see cref="PdfPageSource.Text"/>；若文字全為空白且頁面包含圖片則回傳
/// <see cref="PdfPageSource.Ocr"/> 並帶空字串，由 caller 自行串接 <see cref="IOcrAdapter"/>。</para>
/// <para>此 parser 不執行 OCR；圖片模式頁面的 OcrConfidence 一律為 null，留待 OCR adapter 補上。</para>
/// </summary>
public sealed class PdfPigStatementParser : IPdfStatementParser
{
    public async Task<IReadOnlyList<PdfPage>> ExtractPagesAsync(
        Stream content,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var buffer = await CopyToMemoryAsync(content, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        return ExtractPagesCore(buffer);
    }

    private static IReadOnlyList<PdfPage> ExtractPagesCore(byte[] buffer)
    {
        using var document = PdfDocument.Open(buffer);
        var pages = new List<PdfPage>(document.NumberOfPages);

        foreach (var page in document.GetPages())
        {
            var text = ContentOrderTextExtractor.GetText(page) ?? string.Empty;
            var hasText = !string.IsNullOrWhiteSpace(text);

            byte[]? imageBytes = null;
            var source = hasText
                ? PdfPageSource.Text
                : DetectImageSource(page, out imageBytes);

            pages.Add(new PdfPage(
                PageIndex: page.Number - 1,
                Text: hasText ? text : string.Empty,
                Source: source,
                OcrConfidence: null,
                ImageBytes: imageBytes));
        }

        return pages;
    }

    private static PdfPageSource DetectImageSource(Page page, out byte[]? imageBytes)
    {
        imageBytes = null;
        var firstImage = page.GetImages().FirstOrDefault();
        if (firstImage is null)
        {
            return PdfPageSource.Text;
        }

        if (firstImage.TryGetPng(out var png) && png is not null && png.Length > 0)
        {
            imageBytes = png;
        }
        else if (!firstImage.RawMemory.IsEmpty)
        {
            imageBytes = firstImage.RawMemory.ToArray();
        }

        return PdfPageSource.Ocr;
    }

    private static async Task<byte[]> CopyToMemoryAsync(Stream content, CancellationToken ct)
    {
        if (content is MemoryStream { CanSeek: true } existing)
        {
            return existing.ToArray();
        }

        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct).ConfigureAwait(false);
        return ms.ToArray();
    }
}
