using Assetra.Core.Interfaces.Import;
using Assetra.Core.Models.Import;
using Assetra.Infrastructure.Import.Pdf;

namespace Assetra.Infrastructure.Import;

/// <summary>
/// 依 (Format, FileType) 取得對應的 <see cref="IImportParser"/>。
/// 目前所有格式共用 <see cref="ConfigurableCsvParser"/> / <see cref="ConfigurableExcelParser"/>，
/// 差異由 config 提供。需要為某家銀行寫自訂 parser 時，
/// 在這裡加 case 即可，不會影響其他家。
/// <para>v0.19.3 起支援 PDF：caller 透過 DI 提供 <see cref="IPdfStatementParser"/>（必填，否則 PDF 路徑會 throw）
/// 與可選 OCR factory（圖片模式 PDF 才需要）。Csv / Excel 路徑不受影響。</para>
/// <para>v0.19.4 將 OCR adapter 改為 <see cref="Func{IOcrAdapter}"/>，於 <see cref="Create"/> 時動態解析，
/// 讓設定（tessdata path / language）改變後不需重啟即可生效。</para>
/// </summary>
public sealed class ImportParserFactory
{
    private readonly IPdfStatementParser? _pdfParser;
    private readonly Func<IOcrAdapter?>? _ocrAdapterFactory;

    public ImportParserFactory()
        : this(pdfParser: null, ocrAdapterFactory: null)
    {
    }

    public ImportParserFactory(IPdfStatementParser? pdfParser, Func<IOcrAdapter?>? ocrAdapterFactory = null)
    {
        _pdfParser = pdfParser;
        _ocrAdapterFactory = ocrAdapterFactory;
    }

    /// <summary>Back-compat overload — keeps a static <see cref="IOcrAdapter"/> instance.</summary>
    public ImportParserFactory(IPdfStatementParser? pdfParser, IOcrAdapter? ocrAdapter)
        : this(pdfParser, ocrAdapter is null ? null : (Func<IOcrAdapter?>)(() => ocrAdapter))
    {
    }

    public IImportParser Create(ImportFormat format, ImportFileType fileType) => fileType switch
    {
        ImportFileType.Csv => new ConfigurableCsvParser(CsvParserConfigs.For(format)),
        ImportFileType.Excel => new ConfigurableExcelParser(ExcelParserConfigs.For(format)),
        ImportFileType.Pdf => CreatePdfParser(format),
        _ => throw new ArgumentOutOfRangeException(nameof(fileType), fileType, "Unsupported file type"),
    };

    private PdfImportParser CreatePdfParser(ImportFormat format)
    {
        if (_pdfParser is null)
        {
            throw new InvalidOperationException(
                "PDF import requires an IPdfStatementParser to be registered with the factory.");
        }
        var ocr = _ocrAdapterFactory?.Invoke();
        return new PdfImportParser(_pdfParser, PdfRowPatterns.For(format), ocr, format);
    }
}
