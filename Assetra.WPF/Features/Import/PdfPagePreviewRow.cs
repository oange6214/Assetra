using Assetra.Core.Models.Import;

namespace Assetra.WPF.Features.Import;

/// <summary>
/// 顯示用：單頁 PDF 的解析結果（文字模式 vs OCR + 信心分數 + 抽取文字）。
/// 由 <see cref="ImportViewModel.PopulatePdfPreviewAsync"/> 填入；輔助使用者驗證 OCR 結果。
/// </summary>
public sealed record PdfPagePreviewRow(
    int PageIndex,
    PdfPageSource Source,
    double? OcrConfidence,
    string Text)
{
    public string SourceLabel => Source switch
    {
        PdfPageSource.Text => "Text",
        PdfPageSource.Ocr => "OCR",
        _ => Source.ToString(),
    };

    /// <summary>例：「OCR 信心 87%」或文字模式時為空字串。</summary>
    public string ConfidenceDisplay =>
        OcrConfidence is { } c ? $"{c * 100.0:N0}%" : string.Empty;

    /// <summary>True 當 OCR 信心 &lt; 50%（XAML binding 用來顯示警告色）。</summary>
    public bool IsLowConfidence =>
        OcrConfidence is { } c && c < 0.5;

    /// <summary>截短文字預覽避免大量 PDF 撐爆 UI（最多 4000 字）。</summary>
    public string TextPreview =>
        Text.Length <= 4000 ? Text : Text[..4000] + "…";

    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}
