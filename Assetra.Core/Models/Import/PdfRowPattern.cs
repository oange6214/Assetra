using System.Text.RegularExpressions;

namespace Assetra.Core.Models.Import;

/// <summary>
/// PDF 對帳單的行級擷取規則（純設定，不含程式邏輯）。
/// <para>每張 <see cref="PdfPage"/> 的 <see cref="PdfPage.Text"/> 會用 <see cref="LinePattern"/> 逐行匹配，
/// 命名群組 <c>date</c> / <c>amount</c> / <c>counterparty</c>（可選 <c>memo</c>）抽出對應欄位。</para>
/// <para>低於 <see cref="MinOcrConfidence"/> 的 OCR 頁面預設略過；caller 可自行降低門檻或全收後標紅。</para>
/// </summary>
public sealed record PdfRowPattern(
    Regex LinePattern,
    string DateFormat,
    /// <summary>0~1 之間。OCR 來源頁面 <see cref="PdfPage.OcrConfidence"/> 低於此值會被略過。文字來源不受影響。</summary>
    double MinOcrConfidence = 0.7,
    /// <summary>金額正負慣例：true 表示來源檔案的負號代表支出（一般銀行對帳），false 表示一律取絕對值（少數券商）。</summary>
    bool PreserveSign = true);
