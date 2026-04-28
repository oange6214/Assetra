using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models.Import;
using Tesseract;

namespace Assetra.Infrastructure.Import.Pdf;

/// <summary>
/// Tesseract.NET 實作的 <see cref="IOcrAdapter"/>。
/// <para>呼叫者需提供 <c>tessdata</c> 目錄路徑（含 <c>eng.traineddata</c> 等語言資料），
/// 與要識別的 language code（多語言用 <c>+</c> 串接，如 <c>"eng+chi_tra"</c>）。</para>
/// <para>真實識別在 <see cref="Task.Run"/> 上執行，避免阻塞 caller 的同步上下文（Tesseract API 為同步、CPU-bound）。</para>
/// </summary>
public sealed class TesseractOcrAdapter : IOcrAdapter
{
    private readonly string _tessdataPath;
    private readonly string _language;
    private readonly Func<byte[], CancellationToken, OcrResult>? _recognizeOverride;

    public TesseractOcrAdapter(string tessdataPath, string language = "eng")
        : this(tessdataPath, language, recognizeOverride: null)
    {
    }

    internal TesseractOcrAdapter(
        string tessdataPath,
        string language,
        Func<byte[], CancellationToken, OcrResult>? recognizeOverride)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tessdataPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        _tessdataPath = tessdataPath;
        _language = language;
        _recognizeOverride = recognizeOverride;
    }

    public Task<OcrResult> RecognizeAsync(
        ReadOnlyMemory<byte> imageBytes,
        CancellationToken ct = default)
    {
        if (imageBytes.IsEmpty)
        {
            throw new ArgumentException("Image bytes cannot be empty.", nameof(imageBytes));
        }

        ct.ThrowIfCancellationRequested();

        var buffer = imageBytes.ToArray();
        return Task.Run(() => Recognize(buffer, ct), ct);
    }

    private OcrResult Recognize(byte[] bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (_recognizeOverride is not null)
        {
            return _recognizeOverride(bytes, ct);
        }

        using var engine = new TesseractEngine(_tessdataPath, _language, EngineMode.Default);
        using var img = Pix.LoadFromMemory(bytes);
        using var page = engine.Process(img);

        var text = page.GetText() ?? string.Empty;
        var confidence = ClampConfidence(page.GetMeanConfidence());

        return new OcrResult(text.Trim(), confidence);
    }

    private static double ClampConfidence(float raw) =>
        raw switch
        {
            float.NaN => 0d,
            < 0f => 0d,
            > 1f => 1d,
            _ => raw,
        };
}
