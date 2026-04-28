using System.IO;
using System.Threading;
using Assetra.Core.Models.Import;
using Assetra.Infrastructure.Import.Pdf;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Assetra.Tests.Infrastructure.Import;

public sealed class PdfPigStatementParserTests
{
    private static byte[] BuildPdf(params string[] pageTexts)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var text in pageTexts)
        {
            var page = builder.AddPage(595, 842);
            if (!string.IsNullOrEmpty(text))
            {
                page.AddText(text, 12, new PdfPoint(50, 750), font);
            }
        }

        return builder.Build();
    }

    [Fact]
    public async Task ExtractPagesAsync_SinglePageWithText_ReturnsTextSource()
    {
        var pdf = BuildPdf("2026-04-01 STARBUCKS -120");
        await using var ms = new MemoryStream(pdf);

        var parser = new PdfPigStatementParser();
        var pages = await parser.ExtractPagesAsync(ms);

        Assert.Single(pages);
        Assert.Equal(0, pages[0].PageIndex);
        Assert.Equal(PdfPageSource.Text, pages[0].Source);
        Assert.Contains("STARBUCKS", pages[0].Text);
        Assert.Null(pages[0].OcrConfidence);
    }

    [Fact]
    public async Task ExtractPagesAsync_MultiplePages_AssignsZeroBasedPageIndex()
    {
        var pdf = BuildPdf("Page A content", "Page B content", "Page C content");
        await using var ms = new MemoryStream(pdf);

        var parser = new PdfPigStatementParser();
        var pages = await parser.ExtractPagesAsync(ms);

        Assert.Equal(3, pages.Count);
        Assert.Equal(0, pages[0].PageIndex);
        Assert.Equal(1, pages[1].PageIndex);
        Assert.Equal(2, pages[2].PageIndex);
        Assert.Contains("A", pages[0].Text);
        Assert.Contains("B", pages[1].Text);
        Assert.Contains("C", pages[2].Text);
    }

    [Fact]
    public async Task ExtractPagesAsync_BlankPageWithoutImages_TreatedAsTextSourceEmpty()
    {
        var pdf = BuildPdf("");
        await using var ms = new MemoryStream(pdf);

        var parser = new PdfPigStatementParser();
        var pages = await parser.ExtractPagesAsync(ms);

        Assert.Single(pages);
        Assert.Equal(PdfPageSource.Text, pages[0].Source);
        Assert.Equal(string.Empty, pages[0].Text);
    }

    [Fact]
    public async Task ExtractPagesAsync_NonSeekableStream_StillWorks()
    {
        var pdf = BuildPdf("Some text");
        await using var inner = new MemoryStream(pdf);
        await using var nonSeekable = new NonSeekableStream(inner);

        var parser = new PdfPigStatementParser();
        var pages = await parser.ExtractPagesAsync(nonSeekable);

        Assert.Single(pages);
        Assert.Contains("Some text", pages[0].Text);
    }

    [Fact]
    public async Task ExtractPagesAsync_NullStream_Throws()
    {
        var parser = new PdfPigStatementParser();
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => parser.ExtractPagesAsync(null!));
    }

    [Fact]
    public async Task ExtractPagesAsync_RespectsCancellation()
    {
        var pdf = BuildPdf("Content");
        await using var ms = new MemoryStream(pdf);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var parser = new PdfPigStatementParser();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => parser.ExtractPagesAsync(ms, cts.Token));
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
