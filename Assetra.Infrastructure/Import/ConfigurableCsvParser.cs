using System.Globalization;
using System.Text;
using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models.Import;
using CsvHelper;
using CsvHelper.Configuration;

namespace Assetra.Infrastructure.Import;

/// <summary>
/// 以 <see cref="CsvParserConfig"/> 驅動的通用 CSV 解析器。
/// 同一個實作可以服務 Generic + 5 銀行 + 5 券商共 11 種格式——
/// 不同格式的差異全部封裝在 config，符合「改 config 不改 code」原則。
/// </summary>
public sealed class ConfigurableCsvParser : IImportParser
{
    private readonly CsvParserConfig _config;

    static ConfigurableCsvParser()
    {
        // 註冊 big5 等非 UTF 編碼支援。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public ConfigurableCsvParser(CsvParserConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public ImportFormat Format => _config.Format;
    public ImportFileType FileType => ImportFileType.Csv;

    public Task<IReadOnlyList<ImportPreviewRow>> ParseAsync(
        Stream content,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var encoding = ResolveEncoding(_config.EncodingName);
        using var reader = new StreamReader(content, encoding, leaveOpen: true);

        for (var i = 0; i < _config.SkipRowsBeforeHeader; i++)
        {
            reader.ReadLine();
        }

        var csvCfg = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = _config.HasHeader,
            Delimiter = _config.Delimiter.ToString(),
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null,
        };

        using var csv = new CsvReader(reader, csvCfg);
        if (_config.HasHeader)
        {
            csv.Read();
            csv.ReadHeader();
        }

        var results = new List<ImportPreviewRow>();
        var index = 0;
        while (csv.Read())
        {
            ct.ThrowIfCancellationRequested();
            index++;

            var row = TryBuildRow(csv, index);
            if (row is not null)
            {
                results.Add(row);
            }
        }

        return Task.FromResult<IReadOnlyList<ImportPreviewRow>>(results);
    }

    private ImportPreviewRow? TryBuildRow(CsvReader csv, int rowIndex)
    {
        var dateText = Field(csv, _config.DateColumn);
        if (string.IsNullOrWhiteSpace(dateText)) return null;

        if (!TryParseDate(dateText, out var date)) return null;

        if (!TryResolveAmount(csv, out var amount)) return null;

        return new ImportPreviewRow(
            RowIndex: rowIndex,
            Date: date,
            Amount: amount,
            Counterparty: Field(csv, _config.CounterpartyColumn),
            Memo: Field(csv, _config.MemoColumn),
            Symbol: Field(csv, _config.SymbolColumn),
            Quantity: ParseDecimal(Field(csv, _config.QuantityColumn)));
    }

    private bool TryResolveAmount(CsvReader csv, out decimal amount)
    {
        amount = 0m;

        if (!string.IsNullOrEmpty(_config.AmountColumn))
        {
            return TryParseDecimal(Field(csv, _config.AmountColumn), out amount);
        }

        // 銀行借/貸分欄：credit (存入) - debit (提出)
        var debit = ParseDecimal(Field(csv, _config.DebitColumn)) ?? 0m;
        var credit = ParseDecimal(Field(csv, _config.CreditColumn)) ?? 0m;

        if (debit == 0m && credit == 0m) return false;

        amount = credit - debit;
        return true;
    }

    private bool TryParseDate(string text, out DateOnly date)
    {
        var trimmed = text.Trim();
        if (DateOnly.TryParseExact(trimmed, _config.DateFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date))
        {
            return true;
        }

        // 寬鬆 fallback：常見日期格式
        if (DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        date = default;
        return false;
    }

    private static string? Field(CsvReader csv, string? column)
    {
        if (string.IsNullOrEmpty(column)) return null;

        try
        {
            return csv.GetField(column);
        }
        catch
        {
            return null;
        }
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

    private static Encoding ResolveEncoding(string name) => name.ToLowerInvariant() switch
    {
        "utf-8" or "utf8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        "big5" => Encoding.GetEncoding("big5"),
        _ => Encoding.GetEncoding(name),
    };
}
