using System.IO;
using System.Threading;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models.Import;
using Assetra.Infrastructure.Import;
using Assetra.Infrastructure.Import.Pdf;
using Xunit;

namespace Assetra.Tests.Infrastructure.Import;

public sealed class ImportParserFactoryTests
{
    private sealed class StubPdfParser : IPdfStatementParser
    {
        public Task<IReadOnlyList<PdfPage>> ExtractPagesAsync(Stream content, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PdfPage>>(Array.Empty<PdfPage>());
    }

    private sealed class StubOcr : IOcrAdapter
    {
        public Task<OcrResult> RecognizeAsync(ReadOnlyMemory<byte> imageBytes, CancellationToken ct = default) =>
            Task.FromResult(new OcrResult(string.Empty, 0));
    }

    [Fact]
    public void Create_PdfWithoutPdfParser_Throws()
    {
        var factory = new ImportParserFactory();
        Assert.Throws<InvalidOperationException>(() =>
            factory.Create(ImportFormat.Generic, ImportFileType.Pdf));
    }

    [Fact]
    public void Create_Pdf_InvokesOcrFactoryEachCall()
    {
        var calls = 0;
        var factory = new ImportParserFactory(new StubPdfParser(), () =>
        {
            calls++;
            return new StubOcr();
        });

        _ = factory.Create(ImportFormat.Generic, ImportFileType.Pdf);
        _ = factory.Create(ImportFormat.Generic, ImportFileType.Pdf);

        Assert.Equal(2, calls);
    }

    [Fact]
    public void Create_Pdf_OcrFactoryReturningNull_StillProducesParser()
    {
        var factory = new ImportParserFactory(new StubPdfParser(), () => null);
        var parser = factory.Create(ImportFormat.Generic, ImportFileType.Pdf);

        Assert.IsType<PdfImportParser>(parser);
        Assert.Equal(ImportFileType.Pdf, parser.FileType);
    }

    [Fact]
    public void Create_Csv_DoesNotInvokeOcrFactory()
    {
        var calls = 0;
        var factory = new ImportParserFactory(new StubPdfParser(), () =>
        {
            calls++;
            return new StubOcr();
        });

        _ = factory.Create(ImportFormat.Generic, ImportFileType.Csv);

        Assert.Equal(0, calls);
    }
}
