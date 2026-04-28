namespace Assetra.Core.Models.Import;

/// <summary>
/// OCR adapter 識別單張圖片的回傳結果。
/// <para><see cref="Confidence"/> 為 0~1 的平均信心分數；越接近 1 表示識別越可靠。
/// caller 可用 <c>PdfRowPattern.MinOcrConfidence</c> 篩除低信心頁面或標紅供使用者確認。</para>
/// </summary>
public sealed record OcrResult(
    string Text,
    double Confidence);
