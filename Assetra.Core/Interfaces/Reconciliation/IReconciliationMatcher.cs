using Assetra.Core.Models;
using Assetra.Core.Models.Import;

namespace Assetra.Core.Interfaces.Reconciliation;

/// <summary>
/// 將「對帳單原始列」(<see cref="ImportPreviewRow"/>) 與既有 <see cref="Trade"/> 做模糊比對。
/// <para>
/// 比對嚴格度比 <c>ImportConflictDetector</c> 的去重 hash 寬鬆 — 容忍日期落差（銀行假日入帳延遲）
/// 與小數四捨五入差異。實作要保證 <see cref="DateToleranceDays"/> 與 <see cref="AmountTolerance"/>
/// 為 immutable，且任何兩列的比對結果僅依輸入決定（無外部狀態）。
/// </para>
/// </summary>
public interface IReconciliationMatcher
{
    /// <summary>日期容忍度（天）；預設 1。</summary>
    int DateToleranceDays { get; }

    /// <summary>金額絕對值容忍度；預設 0.005（涵蓋四捨五入）。</summary>
    decimal AmountTolerance { get; }

    /// <summary>對帳單列的有號金額（正號為流入、負號為流出）。</summary>
    decimal SignedAmount(ImportPreviewRow row);

    /// <summary>交易的有號金額（正號為流入、負號為流出）。</summary>
    decimal SignedAmount(Trade trade);

    /// <summary>交易的對帳基準日（取 <see cref="Trade.TradeDate"/> 的 DateOnly）。</summary>
    DateOnly DateOf(Trade trade);

    /// <summary>金額（abs）+ 同號 + 日期落差在容忍度內，視為候選配對。</summary>
    bool IsMatch(ImportPreviewRow row, Trade trade);

    /// <summary>
    /// 兩個金額是否在 <see cref="AmountTolerance"/> 容忍度內視為相等。
    /// 比較使用 abs 值（不看號）。
    /// </summary>
    bool AmountClose(decimal a, decimal b);
}
