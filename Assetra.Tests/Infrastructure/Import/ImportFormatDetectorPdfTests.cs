using System.IO;
using System.Text;
using Assetra.Core.Models.Import;
using Assetra.Infrastructure.Import;
using Xunit;

namespace Assetra.Tests.Infrastructure.Import;

public sealed class ImportFormatDetectorPdfTests
{
    [Fact]
    public async Task DetectAsync_PdfMagicBytes_ReturnsGeneric()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.7\n...rest");
        await using var ms = new MemoryStream(bytes);

        var detector = new ImportFormatDetector();
        var result = await detector.DetectAsync("statement.pdf", ms);

        Assert.Equal(ImportFormat.Generic, result);
    }

    [Fact]
    public async Task DetectAsync_PdfExtensionButWrongMagic_ReturnsNull()
    {
        var bytes = Encoding.ASCII.GetBytes("NOTPDF\n...");
        await using var ms = new MemoryStream(bytes);

        var detector = new ImportFormatDetector();
        var result = await detector.DetectAsync("fake.pdf", ms);

        Assert.Null(result);
    }

    [Fact]
    public async Task DetectAsync_PdfDetection_LeavesStreamPositionUnchanged()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.4\nfollowing");
        await using var ms = new MemoryStream(bytes);
        ms.Position = 0;

        var detector = new ImportFormatDetector();
        await detector.DetectAsync("a.pdf", ms);

        Assert.Equal(0, ms.Position);
    }
}
