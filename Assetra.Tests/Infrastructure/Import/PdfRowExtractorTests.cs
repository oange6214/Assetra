using System.Text.RegularExpressions;
using Assetra.Core.Models.Import;
using Assetra.Infrastructure.Import.Pdf;
using Xunit;

namespace Assetra.Tests.Infrastructure.Import;

public sealed class PdfRowExtractorTests
{
    private static readonly Regex DefaultPattern = new(
        @"^(?<date>\d{4}-\d{2}-\d{2})\s+(?<counterparty>.+?)\s+(?<amount>-?[\d,]+(?:\.\d+)?)$",
        RegexOptions.Compiled);

    private static PdfRowPattern MakePattern(
        Regex? regex = null,
        double minConfidence = 0.7,
        bool preserveSign = true) =>
        new(regex ?? DefaultPattern, "yyyy-MM-dd", minConfidence, preserveSign);

    private static PdfPage TextPage(int index, string text) =>
        new(index, text, PdfPageSource.Text);

    private static PdfPage OcrPage(int index, string text, double confidence) =>
        new(index, text, PdfPageSource.Ocr, confidence);

    [Fact]
    public void Extract_EmptyPages_ReturnsEmpty()
    {
        var rows = PdfRowExtractor.Extract(Array.Empty<PdfPage>(), MakePattern());
        Assert.Empty(rows);
    }

    [Fact]
    public void Extract_SingleTextPage_ParsesMatchingLines()
    {
        var page = TextPage(0,
            "2026-04-01 STARBUCKS -120.00\n2026-04-02 SALARY 50,000.00\n");
        var rows = PdfRowExtractor.Extract(new[] { page }, MakePattern());

        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateOnly(2026, 4, 1), rows[0].Date);
        Assert.Equal(-120m, rows[0].Amount);
        Assert.Equal("STARBUCKS", rows[0].Counterparty);
        Assert.Equal(50000m, rows[1].Amount);
    }

    [Fact]
    public void Extract_SkipsBlankAndUnmatchedLines()
    {
        var page = TextPage(0,
            "Statement Header\n\n2026-04-01 SHOP -50.00\nfooter\n");
        var rows = PdfRowExtractor.Extract(new[] { page }, MakePattern());

        Assert.Single(rows);
        Assert.Equal(-50m, rows[0].Amount);
    }

    [Fact]
    public void Extract_ConcatenatesAcrossPages_AndAssignsSequentialRowIndex()
    {
        var p1 = TextPage(0, "2026-04-01 A -10.00");
        var p2 = TextPage(1, "2026-04-02 B -20.00\n2026-04-03 C -30.00");
        var rows = PdfRowExtractor.Extract(new[] { p1, p2 }, MakePattern());

        Assert.Equal(3, rows.Count);
        Assert.Equal(0, rows[0].RowIndex);
        Assert.Equal(1, rows[1].RowIndex);
        Assert.Equal(2, rows[2].RowIndex);
    }

    [Fact]
    public void Extract_SkipsOcrPagesBelowConfidenceThreshold()
    {
        var lowConf = OcrPage(0, "2026-04-01 BLURRY -10.00", 0.4);
        var highConf = OcrPage(1, "2026-04-02 CLEAR -20.00", 0.9);
        var rows = PdfRowExtractor.Extract(new[] { lowConf, highConf }, MakePattern());

        Assert.Single(rows);
        Assert.Equal("CLEAR", rows[0].Counterparty);
    }

    [Fact]
    public void Extract_OcrWithoutConfidenceTreatedAsZero_AndSkipped()
    {
        var page = new PdfPage(0, "2026-04-01 X -1.00", PdfPageSource.Ocr, OcrConfidence: null);
        var rows = PdfRowExtractor.Extract(new[] { page }, MakePattern());
        Assert.Empty(rows);
    }

    [Fact]
    public void Extract_TextPageNotAffectedByConfidenceThreshold()
    {
        var page = TextPage(0, "2026-04-01 X -1.00");
        var rows = PdfRowExtractor.Extract(new[] { page }, MakePattern(minConfidence: 0.99));
        Assert.Single(rows);
    }

    [Fact]
    public void Extract_PreserveSignFalse_TakesAbsoluteValue()
    {
        var page = TextPage(0, "2026-04-01 X -120.00\n2026-04-02 Y 50.00");
        var rows = PdfRowExtractor.Extract(new[] { page }, MakePattern(preserveSign: false));

        Assert.Equal(120m, rows[0].Amount);
        Assert.Equal(50m, rows[1].Amount);
    }

    [Fact]
    public void Extract_InvalidDate_SkipsRow()
    {
        var pattern = new Regex(
            @"^(?<date>\S+)\s+(?<counterparty>\S+)\s+(?<amount>-?\d+)$");
        var page = TextPage(0, "not-a-date FOO -10");
        var rows = PdfRowExtractor.Extract(new[] { page }, MakePattern(pattern));
        Assert.Empty(rows);
    }

    [Fact]
    public void Extract_AmountWithThousandsSeparator_Parses()
    {
        var page = TextPage(0, "2026-04-01 RENT -25,000.50");
        var rows = PdfRowExtractor.Extract(new[] { page }, MakePattern());
        Assert.Equal(-25000.50m, rows[0].Amount);
    }

    [Fact]
    public void Extract_OptionalMemoGroupCaptured()
    {
        var pattern = new Regex(
            @"^(?<date>\d{4}-\d{2}-\d{2})\s+(?<counterparty>\S+)\s+(?<amount>-?\d+)\s+(?<memo>.+)$");
        var page = TextPage(0, "2026-04-01 SHOP -10 monthly");
        var rows = PdfRowExtractor.Extract(new[] { page }, MakePattern(pattern));

        Assert.Single(rows);
        Assert.Equal("monthly", rows[0].Memo);
    }

    [Fact]
    public void Extract_NullPagesThrows()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PdfRowExtractor.Extract(null!, MakePattern()));
    }

    [Fact]
    public void Extract_NullPatternThrows()
    {
        Assert.Throws<ArgumentNullException>(() =>
            PdfRowExtractor.Extract(Array.Empty<PdfPage>(), null!));
    }
}
