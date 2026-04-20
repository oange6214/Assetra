using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

/// <summary>
/// 統一的交易記錄（journal）寫入入口。
///
/// <para>
/// 自 2026-04 起採用<b>單一真相</b>（single source of truth）架構：
/// 本服務只負責驗證並 AddAsync / UpdateAsync / RemoveAsync <see cref="Trade"/>。
/// 帳戶餘額（CashAccount / LiabilityAccount）<b>不再</b>由本服務同步更新；
/// 所有餘額查詢皆須透過 <see cref="IBalanceQueryService"/> 由交易歷史投影。
/// </para>
///
/// <para>消費端優先透過此 Service 進行交易操作，而非直接呼叫
/// <see cref="ITradeRepository"/>，以確保：</para>
/// <list type="bullet">
///   <item>業務規則（欄位契約）集中驗證（如 Loan* 必填 LoanLabel、Transfer 必填 ToCashAccountId）</item>
///   <item>未來加入 audit log / undo / event bus 時只需改這一層</item>
/// </list>
/// </summary>
public interface ITransactionService
{
    /// <summary>
    /// 新增一筆交易。欄位契約違規時拋出 <see cref="InvalidOperationException"/>。
    /// </summary>
    Task RecordAsync(Trade trade);

    /// <summary>刪除一筆交易。餘額將由下次投影自動反映。</summary>
    Task DeleteAsync(Trade trade);

    /// <summary>以 <paramref name="replacement"/> 取代 <paramref name="original"/> 的交易內容。</summary>
    Task ReplaceAsync(Trade original, Trade replacement);
}
