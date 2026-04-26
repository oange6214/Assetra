using System.IO;
using System.Text;
using Assetra.Core.Models.Import;
using Assetra.Infrastructure.Import;
using Xunit;

namespace Assetra.Tests.Infrastructure.Import;

public class ImportFormatDetectorTests
{
    [Fact]
    public async Task Detects_ByFileNameSignature_Cathay()
    {
        var detector = new ImportFormatDetector();
        using var ms = new MemoryStream();
        var format = await detector.DetectAsync("cathay-statement-202604.csv", ms);
        Assert.Equal(ImportFormat.CathayUnitedBank, format);
    }

    [Fact]
    public async Task Detects_ByHeaderSignature_Esun()
    {
        var detector = new ImportFormatDetector();
        var bytes = Encoding.GetEncoding("big5").GetBytes("交易日期,支出,存入,說明,備註\n");
        using var ms = new MemoryStream(bytes);

        var format = await detector.DetectAsync("statement.csv", ms);

        Assert.Equal(ImportFormat.EsunBank, format);
    }

    [Fact]
    public async Task FallsBackToGeneric_WhenHeaderHasDateAndAmount()
    {
        var detector = new ImportFormatDetector();
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("Date,Amount,Memo\n"));
        var format = await detector.DetectAsync("statement.csv", ms);
        Assert.Equal(ImportFormat.Generic, format);
    }

    [Fact]
    public async Task ReturnsNull_WhenNoSignatureMatches()
    {
        var detector = new ImportFormatDetector();
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("foo,bar,baz\n"));
        var format = await detector.DetectAsync("random.csv", ms);
        Assert.Null(format);
    }
}
