using Assetra.Core.Models.Import;

namespace Assetra.Infrastructure.Import;

/// <summary>
/// 宣告式 CSV 解析設定。要修正一家銀行/券商的 parser，
/// 直接改 <see cref="CsvParserConfigs"/> 內對應的設定即可，無需動 parser 程式碼。
/// </summary>
public sealed record CsvParserConfig
{
    public required ImportFormat Format { get; init; }
    public string EncodingName { get; init; } = "utf-8";       // "big5" / "utf-8"
    public char Delimiter { get; init; } = ',';
    public int SkipRowsBeforeHeader { get; init; } = 0;
    public bool HasHeader { get; init; } = true;

    /// <summary>日期欄位名（HasHeader=true）或欄位索引字串（如 "0"）。</summary>
    public required string DateColumn { get; init; }

    /// <summary>單一金額欄位（正負號表示收/支）。若銀行用借/貸分欄請改用 <see cref="DebitColumn"/> + <see cref="CreditColumn"/>。</summary>
    public string? AmountColumn { get; init; }

    /// <summary>銀行借記欄（提出 / 支出，正數）。</summary>
    public string? DebitColumn { get; init; }

    /// <summary>銀行貸記欄（存入 / 收入，正數）。</summary>
    public string? CreditColumn { get; init; }

    public string? CounterpartyColumn { get; init; }
    public string? MemoColumn { get; init; }
    public string? SymbolColumn { get; init; }
    public string? QuantityColumn { get; init; }
    public string? PriceColumn { get; init; }
    public string? CommissionColumn { get; init; }

    public string DateFormat { get; init; } = "yyyy/MM/dd";

    /// <summary>偵測格式用：標題列若同時包含這些關鍵字即視為符合。空陣列代表不參與自動偵測。</summary>
    public IReadOnlyList<string> HeaderSignature { get; init; } = Array.Empty<string>();

    /// <summary>偵測格式用：檔名包含任一 keyword 即視為符合。</summary>
    public IReadOnlyList<string> FileNameSignature { get; init; } = Array.Empty<string>();
}
