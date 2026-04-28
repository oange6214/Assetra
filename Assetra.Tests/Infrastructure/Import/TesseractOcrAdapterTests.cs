using System.Threading;
using Assetra.Core.Models.Import;
using Assetra.Infrastructure.Import.Pdf;
using Xunit;

namespace Assetra.Tests.Infrastructure.Import;

public sealed class TesseractOcrAdapterTests
{
    private static TesseractOcrAdapter MakeWithFake(
        Func<byte[], CancellationToken, OcrResult> recognize) =>
        new(tessdataPath: "/fake/tessdata", language: "eng", recognizeOverride: recognize);

    [Fact]
    public void Constructor_NullTessdataPath_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TesseractOcrAdapter(null!));
    }

    [Fact]
    public void Constructor_EmptyTessdataPath_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new TesseractOcrAdapter("   "));
    }

    [Fact]
    public void Constructor_EmptyLanguage_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new TesseractOcrAdapter("/tessdata", language: ""));
    }

    [Fact]
    public async Task RecognizeAsync_EmptyImage_Throws()
    {
        var adapter = MakeWithFake((_, _) => new OcrResult("x", 1.0));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            adapter.RecognizeAsync(ReadOnlyMemory<byte>.Empty));
    }

    [Fact]
    public async Task RecognizeAsync_DelegatesToOverride_PassingBytes()
    {
        byte[]? captured = null;
        var adapter = MakeWithFake((bytes, _) =>
        {
            captured = bytes;
            return new OcrResult("hello", 0.85);
        });

        var result = await adapter.RecognizeAsync(new byte[] { 0x10, 0x20, 0x30 });

        Assert.Equal("hello", result.Text);
        Assert.Equal(0.85, result.Confidence);
        Assert.NotNull(captured);
        Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, captured);
    }

    [Fact]
    public async Task RecognizeAsync_PreCancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var adapter = MakeWithFake((_, _) => new OcrResult("x", 1.0));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            adapter.RecognizeAsync(new byte[] { 0x01 }, cts.Token));
    }

    [Fact]
    public async Task RecognizeAsync_RunsOnBackgroundThread()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        int? workerThread = null;
        var adapter = MakeWithFake((_, _) =>
        {
            workerThread = Environment.CurrentManagedThreadId;
            return new OcrResult("x", 1.0);
        });

        await adapter.RecognizeAsync(new byte[] { 0x01 });

        Assert.NotNull(workerThread);
        Assert.NotEqual(callerThread, workerThread);
    }

    [Fact]
    public async Task RecognizeAsync_OverrideReceivesCloneNotOriginalMemory()
    {
        // 確保 caller 修改原 array 後不影響 OCR 任務（adapter 內已 ToArray）。
        var source = new byte[] { 0xAA, 0xBB };
        byte[]? captured = null;
        var adapter = MakeWithFake((bytes, _) =>
        {
            captured = bytes;
            return new OcrResult("ok", 0.9);
        });

        await adapter.RecognizeAsync(source);
        source[0] = 0x00;

        Assert.NotNull(captured);
        Assert.Equal(0xAA, captured![0]);
    }
}
