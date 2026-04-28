using System.Text.RegularExpressions;
using Assetra.Core.Models.Import;

namespace Assetra.Infrastructure.Import.Pdf;

/// <summary>
/// 內建 <see cref="PdfRowPattern"/> registry。每家銀行 / 券商可以新增自家專屬 pattern；
/// 找不到對應 <see cref="ImportFormat"/> 時 fallback 到 <see cref="Generic"/>。
/// </summary>
public static class PdfRowPatterns
{
    /// <summary>
    /// 通用 pattern：<c>YYYY-MM-DD ＜對手方/摘要＞ ＜金額＞</c>。金額允許千分位、可選正負號。
    /// 適合大部分 ASCII 化的銀行 / 信用卡 PDF；中文格式 / 日期分隔符不同的銀行需另寫 pattern。
    /// </summary>
    public static readonly PdfRowPattern Generic = new(
        LinePattern: new Regex(
            @"^(?<date>\d{4}-\d{2}-\d{2})\s+(?<counterparty>.+?)\s+(?<amount>-?[\d,]+(?:\.\d+)?)$",
            RegexOptions.Compiled),
        DateFormat: "yyyy-MM-dd",
        MinOcrConfidence: 0.7,
        PreserveSign: true);

    public static PdfRowPattern For(ImportFormat _) => Generic;
}
