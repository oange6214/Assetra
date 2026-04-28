using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models.Import;

namespace Assetra.Infrastructure.Import.Pdf;

/// <summary>
/// PDF 對應的 <see cref="IImportParser"/>：串接 <see cref="IPdfStatementParser"/>（文字抽取）→
/// 可選的 <see cref="IOcrAdapter"/>（圖片頁 OCR 補強）→ <see cref="PdfRowExtractor"/>（行擷取）。
/// </summary>
/// <remarks>
/// OCR adapter 為可選：未提供時，圖片模式頁面會留空文字，由 <see cref="PdfRowExtractor"/> 自然略過。
/// 已提供時，每張 image-only 頁面會取出第一張內嵌圖（PNG 優先，否則 raw bytes）送 OCR，識別出的文字
/// 與信心分數寫回 <see cref="PdfPage"/>，再由 extractor 套用 <see cref="PdfRowPattern.MinOcrConfidence"/>
/// 過濾。
/// </remarks>
public sealed class PdfImportParser : IImportParser
{
    private readonly IPdfStatementParser _pdfParser;
    private readonly PdfRowPattern _pattern;
    private readonly IOcrAdapter? _ocr;

    public ImportFormat Format { get; }
    public ImportFileType FileType => ImportFileType.Pdf;

    public PdfImportParser(
        IPdfStatementParser pdfParser,
        PdfRowPattern pattern,
        IOcrAdapter? ocr = null,
        ImportFormat format = ImportFormat.Generic)
    {
        ArgumentNullException.ThrowIfNull(pdfParser);
        ArgumentNullException.ThrowIfNull(pattern);

        _pdfParser = pdfParser;
        _pattern = pattern;
        _ocr = ocr;
        Format = format;
    }

    public async Task<IReadOnlyList<ImportPreviewRow>> ParseAsync(
        Stream content,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var pages = await _pdfParser.ExtractPagesAsync(content, ct).ConfigureAwait(false);

        if (_ocr is not null)
        {
            pages = await EnhanceWithOcrAsync(pages, ct).ConfigureAwait(false);
        }

        return PdfRowExtractor.Extract(pages, _pattern);
    }

    private async Task<IReadOnlyList<PdfPage>> EnhanceWithOcrAsync(
        IReadOnlyList<PdfPage> pages,
        CancellationToken ct)
    {
        var enhanced = new List<PdfPage>(pages.Count);
        foreach (var page in pages)
        {
            ct.ThrowIfCancellationRequested();

            if (page.Source != PdfPageSource.Ocr
                || !string.IsNullOrWhiteSpace(page.Text)
                || page.ImageBytes is null
                || page.ImageBytes.Length == 0)
            {
                enhanced.Add(page);
                continue;
            }

            var ocr = await _ocr!.RecognizeAsync(page.ImageBytes, ct).ConfigureAwait(false);
            enhanced.Add(page with
            {
                Text = ocr.Text,
                OcrConfidence = ocr.Confidence,
            });
        }

        return enhanced;
    }
}
