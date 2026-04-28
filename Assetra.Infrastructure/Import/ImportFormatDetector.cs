using System.Text;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models.Import;
using ClosedXML.Excel;

namespace Assetra.Infrastructure.Import;

/// <summary>
/// 自動偵測格式：先看檔名 fingerprint，再看標題列關鍵字。<br/>
/// 兩者皆不命中時回傳 <c>null</c>，由 UI 讓使用者手動選擇。
/// </summary>
public sealed class ImportFormatDetector : IImportFormatDetector
{
    static ImportFormatDetector()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<ImportFormat?> DetectAsync(
        string fileName,
        Stream content,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(content);

        var lowerName = fileName.ToLowerInvariant();

        // 1) 檔名指紋
        foreach (var cfg in CsvParserConfigs.All)
        {
            if (cfg.Format == ImportFormat.Generic) continue;
            if (cfg.FileNameSignature.Any(s => lowerName.Contains(s.ToLowerInvariant())))
            {
                return cfg.Format;
            }
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        // PDF：目前無 per-bank 指紋，magic-byte 確認後一律 fallback Generic
        if (ext == ".pdf")
        {
            return await IsPdfStreamAsync(content, ct).ConfigureAwait(false)
                ? ImportFormat.Generic
                : null;
        }

        // 2) 內容指紋
        var headerKeywords = ext is ".xlsx" or ".xls"
            ? ReadExcelHeader(content)
            : await ReadCsvHeaderAsync(content, ct).ConfigureAwait(false);

        if (headerKeywords.Count == 0) return null;

        foreach (var cfg in CsvParserConfigs.All)
        {
            if (cfg.Format == ImportFormat.Generic) continue;
            if (cfg.HeaderSignature.Count > 0
                && cfg.HeaderSignature.All(sig => headerKeywords.Contains(sig)))
            {
                return cfg.Format;
            }
        }

        // 3) 通用 CSV：含有 Date/Amount header
        if (CsvParserConfigs.Generic.HeaderSignature.All(sig =>
            headerKeywords.Any(h => string.Equals(h, sig, StringComparison.OrdinalIgnoreCase))))
        {
            return ImportFormat.Generic;
        }

        return null;
    }

    private static async Task<HashSet<string>> ReadCsvHeaderAsync(Stream content, CancellationToken ct)
    {
        var bag = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var origin = content.CanSeek ? content.Position : 0L;

        // 嘗試 UTF-8 與 Big5（台灣銀行最常見的兩種編碼）
        foreach (var enc in new[] { Encoding.UTF8, Encoding.GetEncoding("big5") })
        {
            if (content.CanSeek) content.Position = origin;
            using var reader = new StreamReader(content, enc, leaveOpen: true);
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(line)) continue;

            foreach (var cell in line.Split(','))
            {
                bag.Add(cell.Trim().Trim('"'));
            }
        }

        if (content.CanSeek) content.Position = origin;
        return bag;
    }

    private static async Task<bool> IsPdfStreamAsync(Stream content, CancellationToken ct)
    {
        var origin = content.CanSeek ? content.Position : 0L;
        try
        {
            var buffer = new byte[5];
            var read = await content.ReadAsync(buffer.AsMemory(0, 5), ct).ConfigureAwait(false);
            return read == 5
                && buffer[0] == (byte)'%'
                && buffer[1] == (byte)'P'
                && buffer[2] == (byte)'D'
                && buffer[3] == (byte)'F'
                && buffer[4] == (byte)'-';
        }
        finally
        {
            if (content.CanSeek) content.Position = origin;
        }
    }

    private static HashSet<string> ReadExcelHeader(Stream content)
    {
        var bag = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var origin = content.CanSeek ? content.Position : 0L;

        try
        {
            using var workbook = new XLWorkbook(content);
            var sheet = workbook.Worksheet(1);
            var row = sheet.Row(1);
            var lastCol = row.LastCellUsed()?.Address.ColumnNumber ?? 0;
            for (var c = 1; c <= lastCol; c++)
            {
                var v = row.Cell(c).GetString().Trim();
                if (!string.IsNullOrEmpty(v)) bag.Add(v);
            }
        }
        catch
        {
            // 不是合法 xlsx，留給後續手動選擇
        }
        finally
        {
            if (content.CanSeek) content.Position = origin;
        }

        return bag;
    }
}
