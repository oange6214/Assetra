using System.IO;
using System.Threading;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models.Import;
using Assetra.Infrastructure.Import.Pdf;
using Xunit;

namespace Assetra.Tests.Infrastructure.Import;

public sealed class PdfImportParserTests
{
    private sealed class FakePdfParser(IReadOnlyList<PdfPage> pages) : IPdfStatementParser
    {
        public Task<IReadOnlyList<PdfPage>> ExtractPagesAsync(Stream content, CancellationToken ct = default) =>
            Task.FromResult(pages);
    }

    private sealed class FakeOcr(string text, double confidence) : IOcrAdapter
    {
        public int CallCount { get; private set; }

        public Task<OcrResult> RecognizeAsync(ReadOnlyMemory<byte> imageBytes, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(new OcrResult(text, confidence));
        }
    }

    [Fact]
    public async Task ParseAsync_TextOnlyPages_NoOcrCalls_ReturnsRows()
    {
        var pages = new[]
        {
            new PdfPage(0, "2026-04-01 SHOP -100", PdfPageSource.Text),
        };
        var ocr = new FakeOcr("ignored", 0.99);
        var parser = new PdfImportParser(new FakePdfParser(pages), PdfRowPatterns.Generic, ocr);

        await using var ms = new MemoryStream();
        var rows = await parser.ParseAsync(ms);

        Assert.Single(rows);
        Assert.Equal(0, ocr.CallCount);
        Assert.Null(rows[0].OcrConfidence);
    }

    [Fact]
    public async Task ParseAsync_ImagePageWithBytes_OcrEnhancesAndConfidenceFlowsToRow()
    {
        var pages = new[]
        {
            new PdfPage(
                PageIndex: 0,
                Text: string.Empty,
                Source: PdfPageSource.Ocr,
                OcrConfidence: null,
                ImageBytes: new byte[] { 0x01, 0x02 }),
        };
        var ocr = new FakeOcr("2026-04-01 OCR_SHOP -50", 0.92);
        var parser = new PdfImportParser(new FakePdfParser(pages), PdfRowPatterns.Generic, ocr);

        await using var ms = new MemoryStream();
        var rows = await parser.ParseAsync(ms);

        Assert.Equal(1, ocr.CallCount);
        Assert.Single(rows);
        Assert.Equal("OCR_SHOP", rows[0].Counterparty);
        Assert.Equal(0.92, rows[0].OcrConfidence);
    }

    [Fact]
    public async Task ParseAsync_LowConfidenceOcrPage_FilteredByExtractor()
    {
        var pages = new[]
        {
            new PdfPage(0, string.Empty, PdfPageSource.Ocr, OcrConfidence: null,
                ImageBytes: new byte[] { 0xFF }),
        };
        var ocr = new FakeOcr("2026-04-01 BLURRY -10", 0.3);
        var parser = new PdfImportParser(new FakePdfParser(pages), PdfRowPatterns.Generic, ocr);

        await using var ms = new MemoryStream();
        var rows = await parser.ParseAsync(ms);

        Assert.Equal(1, ocr.CallCount);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task ParseAsync_ImagePageWithoutBytes_NoOcrCall_NoRows()
    {
        var pages = new[]
        {
            new PdfPage(0, string.Empty, PdfPageSource.Ocr, OcrConfidence: null, ImageBytes: null),
        };
        var ocr = new FakeOcr("ignored", 0.99);
        var parser = new PdfImportParser(new FakePdfParser(pages), PdfRowPatterns.Generic, ocr);

        await using var ms = new MemoryStream();
        var rows = await parser.ParseAsync(ms);

        Assert.Equal(0, ocr.CallCount);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task ParseAsync_NullOcrAdapter_ImagePagesReturnNoRowsWithoutThrow()
    {
        var pages = new[]
        {
            new PdfPage(0, string.Empty, PdfPageSource.Ocr, OcrConfidence: null,
                ImageBytes: new byte[] { 0x01 }),
            new PdfPage(1, "2026-04-02 X -20", PdfPageSource.Text),
        };
        var parser = new PdfImportParser(new FakePdfParser(pages), PdfRowPatterns.Generic, ocr: null);

        await using var ms = new MemoryStream();
        var rows = await parser.ParseAsync(ms);

        Assert.Single(rows);
        Assert.Equal("X", rows[0].Counterparty);
    }

    [Fact]
    public void Constructor_NullPdfParser_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PdfImportParser(pdfParser: null!, pattern: PdfRowPatterns.Generic));
    }

    [Fact]
    public void Constructor_NullPattern_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PdfImportParser(new FakePdfParser([]), pattern: null!));
    }

    [Fact]
    public void FormatAndFileType_ExposedFromConstructor()
    {
        var parser = new PdfImportParser(
            new FakePdfParser([]),
            PdfRowPatterns.Generic,
            ocr: null,
            format: ImportFormat.Generic);

        Assert.Equal(ImportFormat.Generic, parser.Format);
        Assert.Equal(ImportFileType.Pdf, parser.FileType);
    }
}
