using Assetra.Core.Models.Import;

namespace Assetra.Infrastructure.Import;

/// <summary>
/// Excel 解析設定。欄位對應沿用同一份 <see cref="CsvParserConfig"/>，
/// 只新增 Excel 專屬的 sheet / 標題列位置。
/// </summary>
public sealed record ExcelParserConfig
{
    public required CsvParserConfig Mapping { get; init; }

    /// <summary>工作表名稱；<c>null</c> 表示使用第一張表。</summary>
    public string? SheetName { get; init; }

    /// <summary>標題列的 1-based 列號（多數匯出檔為 1）。</summary>
    public int HeaderRow { get; init; } = 1;

    public ImportFormat Format => Mapping.Format;
}

public static class ExcelParserConfigs
{
    public static readonly ExcelParserConfig Generic = new() { Mapping = CsvParserConfigs.Generic };

    public static readonly ExcelParserConfig CathayUnitedBank = new() { Mapping = CsvParserConfigs.CathayUnitedBank };
    public static readonly ExcelParserConfig EsunBank = new() { Mapping = CsvParserConfigs.EsunBank };
    public static readonly ExcelParserConfig CtbcBank = new() { Mapping = CsvParserConfigs.CtbcBank };
    public static readonly ExcelParserConfig TaishinBank = new() { Mapping = CsvParserConfigs.TaishinBank };
    public static readonly ExcelParserConfig FubonBank = new() { Mapping = CsvParserConfigs.FubonBank };

    public static readonly ExcelParserConfig YuantaSecurities = new() { Mapping = CsvParserConfigs.YuantaSecurities };
    public static readonly ExcelParserConfig FubonSecurities = new() { Mapping = CsvParserConfigs.FubonSecurities };
    public static readonly ExcelParserConfig KgiSecurities = new() { Mapping = CsvParserConfigs.KgiSecurities };
    public static readonly ExcelParserConfig SinoPacSecurities = new() { Mapping = CsvParserConfigs.SinoPacSecurities };
    public static readonly ExcelParserConfig CapitalSecurities = new() { Mapping = CsvParserConfigs.CapitalSecurities };

    public static IReadOnlyList<ExcelParserConfig> All { get; } = new[]
    {
        Generic,
        CathayUnitedBank, EsunBank, CtbcBank, TaishinBank, FubonBank,
        YuantaSecurities, FubonSecurities, KgiSecurities, SinoPacSecurities, CapitalSecurities,
    };

    public static ExcelParserConfig For(ImportFormat format) =>
        All.FirstOrDefault(c => c.Format == format)
        ?? throw new InvalidOperationException($"No Excel config registered for {format}");
}
