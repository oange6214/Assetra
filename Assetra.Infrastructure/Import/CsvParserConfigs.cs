using Assetra.Core.Models.Import;

namespace Assetra.Infrastructure.Import;

/// <summary>
/// 所有 CSV 格式的設定登記表。要新增或修正 parser 行為，請在這裡編輯——
/// 不要在 <see cref="ConfigurableCsvParser"/> 加 if/switch。
/// <para>
/// 註：以下 5 家銀行 + 5 家券商的欄位名稱為**目前根據公開文件與一般慣例的初始預設值**，
/// 上線後拿到真實匯出檔請逐家校準。校準時只需修改本檔，不必動其他程式碼。
/// </para>
/// </summary>
public static class CsvParserConfigs
{
    public static readonly CsvParserConfig Generic = new()
    {
        Format = ImportFormat.Generic,
        DateColumn = "Date",
        AmountColumn = "Amount",
        CounterpartyColumn = "Counterparty",
        MemoColumn = "Memo",
        SymbolColumn = "Symbol",
        QuantityColumn = "Quantity",
        DateFormat = "yyyy-MM-dd",
        HeaderSignature = new[] { "Date", "Amount" },
    };

    // ========== 銀行 ==========

    public static readonly CsvParserConfig CathayUnitedBank = new()
    {
        Format = ImportFormat.CathayUnitedBank,
        EncodingName = "big5",
        DateColumn = "日期",
        DebitColumn = "提出",
        CreditColumn = "存入",
        CounterpartyColumn = "摘要",
        MemoColumn = "備註",
        DateFormat = "yyyy/MM/dd",
        HeaderSignature = new[] { "日期", "提出", "存入" },
        FileNameSignature = new[] { "cathay", "國泰" },
    };

    public static readonly CsvParserConfig EsunBank = new()
    {
        Format = ImportFormat.EsunBank,
        EncodingName = "big5",
        DateColumn = "交易日期",
        DebitColumn = "支出",
        CreditColumn = "存入",
        CounterpartyColumn = "說明",
        MemoColumn = "備註",
        DateFormat = "yyyy/MM/dd",
        HeaderSignature = new[] { "交易日期", "支出", "存入" },
        FileNameSignature = new[] { "esun", "玉山" },
    };

    public static readonly CsvParserConfig CtbcBank = new()
    {
        Format = ImportFormat.CtbcBank,
        EncodingName = "big5",
        DateColumn = "日期",
        DebitColumn = "提款金額",
        CreditColumn = "存款金額",
        CounterpartyColumn = "摘要",
        MemoColumn = "備註",
        DateFormat = "yyyy/MM/dd",
        HeaderSignature = new[] { "日期", "提款金額", "存款金額" },
        FileNameSignature = new[] { "ctbc", "中信", "中國信託" },
    };

    public static readonly CsvParserConfig TaishinBank = new()
    {
        Format = ImportFormat.TaishinBank,
        EncodingName = "big5",
        DateColumn = "交易日",
        DebitColumn = "提款",
        CreditColumn = "存款",
        CounterpartyColumn = "摘要",
        MemoColumn = "備註",
        DateFormat = "yyyy/MM/dd",
        HeaderSignature = new[] { "交易日", "提款", "存款" },
        FileNameSignature = new[] { "taishin", "台新" },
    };

    public static readonly CsvParserConfig FubonBank = new()
    {
        Format = ImportFormat.FubonBank,
        EncodingName = "big5",
        DateColumn = "交易日期",
        DebitColumn = "提出金額",
        CreditColumn = "存入金額",
        CounterpartyColumn = "摘要",
        MemoColumn = "附言",
        DateFormat = "yyyy/MM/dd",
        HeaderSignature = new[] { "交易日期", "提出金額", "存入金額" },
        FileNameSignature = new[] { "fubon-bank", "富邦銀" },
    };

    // ========== 券商 ==========

    public static readonly CsvParserConfig YuantaSecurities = new()
    {
        Format = ImportFormat.YuantaSecurities,
        EncodingName = "big5",
        DateColumn = "成交日期",
        AmountColumn = "成交金額",
        PriceColumn = "成交價",
        CommissionColumn = "手續費",
        CounterpartyColumn = "買賣別",
        SymbolColumn = "股票代號",
        QuantityColumn = "成交股數",
        MemoColumn = "備註",
        DateFormat = "yyyy/MM/dd",
        HeaderSignature = new[] { "成交日期", "股票代號", "成交股數" },
        FileNameSignature = new[] { "yuanta", "元大" },
    };

    public static readonly CsvParserConfig FubonSecurities = new()
    {
        Format = ImportFormat.FubonSecurities,
        EncodingName = "big5",
        DateColumn = "交易日期",
        AmountColumn = "價金",
        PriceColumn = "成交價",
        CommissionColumn = "手續費",
        CounterpartyColumn = "買賣",
        SymbolColumn = "證券代號",
        QuantityColumn = "股數",
        MemoColumn = "備註",
        DateFormat = "yyyy/MM/dd",
        HeaderSignature = new[] { "交易日期", "證券代號", "股數" },
        FileNameSignature = new[] { "fubon-sec", "富邦證" },
    };

    public static readonly CsvParserConfig KgiSecurities = new()
    {
        Format = ImportFormat.KgiSecurities,
        EncodingName = "big5",
        DateColumn = "委託日期",
        AmountColumn = "成交價金",
        PriceColumn = "成交價",
        CommissionColumn = "手續費",
        CounterpartyColumn = "買賣別",
        SymbolColumn = "商品代碼",
        QuantityColumn = "成交數量",
        MemoColumn = "備註",
        DateFormat = "yyyy/MM/dd",
        HeaderSignature = new[] { "委託日期", "商品代碼", "成交數量" },
        FileNameSignature = new[] { "kgi", "凱基" },
    };

    public static readonly CsvParserConfig SinoPacSecurities = new()
    {
        Format = ImportFormat.SinoPacSecurities,
        EncodingName = "big5",
        DateColumn = "交易日",
        AmountColumn = "成交金額",
        PriceColumn = "成交價",
        CommissionColumn = "手續費",
        CounterpartyColumn = "買賣別",
        SymbolColumn = "股票代號",
        QuantityColumn = "成交股數",
        MemoColumn = "備註",
        DateFormat = "yyyy/MM/dd",
        HeaderSignature = new[] { "交易日", "股票代號", "成交股數" },
        FileNameSignature = new[] { "sinopac", "永豐" },
    };

    public static readonly CsvParserConfig CapitalSecurities = new()
    {
        Format = ImportFormat.CapitalSecurities,
        EncodingName = "big5",
        DateColumn = "交易日期",
        AmountColumn = "成交金額",
        PriceColumn = "成交價",
        CommissionColumn = "手續費",
        CounterpartyColumn = "買賣別",
        SymbolColumn = "股票代碼",
        QuantityColumn = "成交股數",
        MemoColumn = "備註",
        DateFormat = "yyyy/MM/dd",
        HeaderSignature = new[] { "交易日期", "股票代碼", "成交股數" },
        FileNameSignature = new[] { "capital", "群益" },
    };

    public static IReadOnlyList<CsvParserConfig> All { get; } = new[]
    {
        Generic,
        CathayUnitedBank, EsunBank, CtbcBank, TaishinBank, FubonBank,
        YuantaSecurities, FubonSecurities, KgiSecurities, SinoPacSecurities, CapitalSecurities,
    };

    public static CsvParserConfig For(ImportFormat format) =>
        All.FirstOrDefault(c => c.Format == format)
        ?? throw new InvalidOperationException($"No CSV config registered for {format}");
}
