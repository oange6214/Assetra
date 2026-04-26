using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models.Import;

namespace Assetra.Infrastructure.Import;

/// <summary>
/// 依 (Format, FileType) 取得對應的 <see cref="IImportParser"/>。
/// 目前所有格式共用 <see cref="ConfigurableCsvParser"/> / <see cref="ConfigurableExcelParser"/>，
/// 差異由 config 提供。需要為某家銀行寫自訂 parser 時，
/// 在這裡加 case 即可，不會影響其他家。
/// </summary>
public sealed class ImportParserFactory
{
    public IImportParser Create(ImportFormat format, ImportFileType fileType) => fileType switch
    {
        ImportFileType.Csv => new ConfigurableCsvParser(CsvParserConfigs.For(format)),
        ImportFileType.Excel => new ConfigurableExcelParser(ExcelParserConfigs.For(format)),
        _ => throw new ArgumentOutOfRangeException(nameof(fileType), fileType, "Unsupported file type"),
    };
}
