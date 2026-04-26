using System.Globalization;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models.Import;
using ClosedXML.Excel;

namespace Assetra.Infrastructure.Import;

/// <summary>
/// 以 <see cref="ExcelParserConfig"/> 驅動的通用 Excel 解析器。
/// 共用 <see cref="CsvParserConfig"/> 的欄位對應邏輯，差異只在 sheet / header row。
/// </summary>
public sealed class ConfigurableExcelParser : IImportParser
{
    private readonly ExcelParserConfig _config;

    public ConfigurableExcelParser(ExcelParserConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public ImportFormat Format => _config.Format;
    public ImportFileType FileType => ImportFileType.Excel;

    public Task<IReadOnlyList<ImportPreviewRow>> ParseAsync(
        Stream content,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var workbook = new XLWorkbook(content);
        var sheet = string.IsNullOrEmpty(_config.SheetName)
            ? workbook.Worksheet(1)
            : workbook.Worksheet(_config.SheetName);

        var headers = ReadHeaders(sheet, _config.HeaderRow);
        var mapping = _config.Mapping;

        var dateCol = ResolveColumn(headers, mapping.DateColumn);
        if (dateCol is null) return Task.FromResult<IReadOnlyList<ImportPreviewRow>>(Array.Empty<ImportPreviewRow>());

        var amountCol = ResolveColumn(headers, mapping.AmountColumn);
        var debitCol = ResolveColumn(headers, mapping.DebitColumn);
        var creditCol = ResolveColumn(headers, mapping.CreditColumn);
        var counterpartyCol = ResolveColumn(headers, mapping.CounterpartyColumn);
        var memoCol = ResolveColumn(headers, mapping.MemoColumn);
        var symbolCol = ResolveColumn(headers, mapping.SymbolColumn);
        var quantityCol = ResolveColumn(headers, mapping.QuantityColumn);

        var results = new List<ImportPreviewRow>();
        var rowIndex = 0;

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? _config.HeaderRow;
        for (var r = _config.HeaderRow + 1; r <= lastRow; r++)
        {
            ct.ThrowIfCancellationRequested();
            if (!TryReadDate(sheet, r, dateCol.Value, mapping.DateFormat, out var date))
                continue;

            if (!TryResolveAmount(sheet, r, amountCol, debitCol, creditCol, out var amount))
                continue;

            rowIndex++;
            results.Add(new ImportPreviewRow(
                RowIndex: rowIndex,
                Date: date,
                Amount: amount,
                Counterparty: counterpartyCol is { } cp ? Cell(sheet, r, cp) : null,
                Memo: memoCol is { } mc ? Cell(sheet, r, mc) : null,
                Symbol: symbolCol is { } sc ? Cell(sheet, r, sc) : null,
                Quantity: quantityCol is { } qc ? ParseDecimal(Cell(sheet, r, qc)) : null));
        }

        return Task.FromResult<IReadOnlyList<ImportPreviewRow>>(results);
    }

    private static Dictionary<string, int> ReadHeaders(IXLWorksheet sheet, int headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var row = sheet.Row(headerRow);
        var lastCol = row.LastCellUsed()?.Address.ColumnNumber ?? 0;
        for (var c = 1; c <= lastCol; c++)
        {
            var key = row.Cell(c).GetString().Trim();
            if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key))
            {
                map[key] = c;
            }
        }
        return map;
    }

    private static int? ResolveColumn(Dictionary<string, int> headers, string? name) =>
        !string.IsNullOrEmpty(name) && headers.TryGetValue(name, out var col) ? col : null;

    private static string Cell(IXLWorksheet sheet, int row, int col) =>
        sheet.Cell(row, col).GetString().Trim();

    private static bool TryResolveAmount(IXLWorksheet sheet, int row, int? amountCol, int? debitCol, int? creditCol, out decimal amount)
    {
        amount = 0m;
        if (amountCol is { } ac)
        {
            return TryParseDecimal(Cell(sheet, row, ac), out amount);
        }
        var debit = ParseDecimal(debitCol is { } dc ? Cell(sheet, row, dc) : null) ?? 0m;
        var credit = ParseDecimal(creditCol is { } cc ? Cell(sheet, row, cc) : null) ?? 0m;
        if (debit == 0m && credit == 0m) return false;
        amount = credit - debit;
        return true;
    }

    private static bool TryReadDate(IXLWorksheet sheet, int row, int col, string format, out DateOnly date)
    {
        var cell = sheet.Cell(row, col);
        if (cell.DataType == XLDataType.DateTime && cell.Value.IsDateTime)
        {
            date = DateOnly.FromDateTime(cell.GetDateTime());
            return true;
        }
        if (cell.DataType == XLDataType.Number && cell.Value.IsNumber)
        {
            try
            {
                date = DateOnly.FromDateTime(DateTime.FromOADate(cell.GetDouble()));
                return true;
            }
            catch
            {
                // fallthrough to text parsing
            }
        }
        var text = cell.GetString().Trim();
        if (string.IsNullOrEmpty(text))
        {
            date = default;
            return false;
        }
        return TryParseDate(text, format, out date);
    }

    private static bool TryParseDate(string text, string format, out DateOnly date)
    {
        var trimmed = text.Trim();
        if (DateOnly.TryParseExact(trimmed, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;
        if (DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;
        date = default;
        return false;
    }

    private static decimal? ParseDecimal(string? text) =>
        TryParseDecimal(text, out var value) ? value : null;

    private static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var cleaned = text.Replace(",", string.Empty).Replace("$", string.Empty)
            .Replace("NT", string.Empty).Trim();
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
