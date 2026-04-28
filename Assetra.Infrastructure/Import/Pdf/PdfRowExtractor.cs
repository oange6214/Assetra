using System.Globalization;
using System.Text.RegularExpressions;
using Assetra.Core.Models.Import;

namespace Assetra.Infrastructure.Import.Pdf;

/// <summary>
/// 純函式：將 <see cref="PdfPage"/> 列表 + <see cref="PdfRowPattern"/> 規則轉為 <see cref="ImportPreviewRow"/> 列表。
/// <para>OCR 來源頁面若 <see cref="PdfPage.OcrConfidence"/> 低於 <see cref="PdfRowPattern.MinOcrConfidence"/> 會整頁略過。
/// 文字來源不受信心門檻影響。</para>
/// <para>逐頁逐行套用 <see cref="PdfRowPattern.LinePattern"/>；命名群組 <c>date</c> / <c>amount</c>
/// 為必要欄位，<c>counterparty</c> / <c>memo</c> 為可選欄位。</para>
/// </summary>
public static class PdfRowExtractor
{
    public static IReadOnlyList<ImportPreviewRow> Extract(
        IReadOnlyList<PdfPage> pages,
        PdfRowPattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(pattern);

        var rows = new List<ImportPreviewRow>();
        var rowIndex = 0;

        foreach (var page in pages)
        {
            if (ShouldSkipPage(page, pattern))
            {
                continue;
            }

            if (string.IsNullOrEmpty(page.Text))
            {
                continue;
            }

            var lines = page.Text.Split('\n', StringSplitOptions.None);
            var pageConfidence = page.Source == PdfPageSource.Ocr ? page.OcrConfidence : null;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var match = pattern.LinePattern.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                if (!TryBuildRow(match, pattern, rowIndex, pageConfidence, out var row))
                {
                    continue;
                }

                rows.Add(row);
                rowIndex++;
            }
        }

        return rows;
    }

    private static bool ShouldSkipPage(PdfPage page, PdfRowPattern pattern) =>
        page.Source == PdfPageSource.Ocr
        && (page.OcrConfidence ?? 0d) < pattern.MinOcrConfidence;

    private static bool TryBuildRow(
        Match match,
        PdfRowPattern pattern,
        int rowIndex,
        double? ocrConfidence,
        out ImportPreviewRow row)
    {
        row = null!;

        var dateText = GroupOrNull(match, "date");
        var amountText = GroupOrNull(match, "amount");
        if (dateText is null || amountText is null)
        {
            return false;
        }

        if (!DateOnly.TryParseExact(
                dateText,
                pattern.DateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return false;
        }

        var normalizedAmount = amountText.Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        if (!decimal.TryParse(
                normalizedAmount,
                NumberStyles.Number | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var amount))
        {
            return false;
        }

        if (!pattern.PreserveSign)
        {
            amount = Math.Abs(amount);
        }

        row = new ImportPreviewRow(
            RowIndex: rowIndex,
            Date: date,
            Amount: amount,
            Counterparty: GroupOrNull(match, "counterparty"),
            Memo: GroupOrNull(match, "memo"),
            OcrConfidence: ocrConfidence);
        return true;
    }

    private static string? GroupOrNull(Match match, string name)
    {
        var group = match.Groups[name];
        if (!group.Success)
        {
            return null;
        }
        var value = group.Value.Trim();
        return value.Length == 0 ? null : value;
    }
}
