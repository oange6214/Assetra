using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface ITradeRepository
{
    /// <summary>全部交易記錄，依日期降冪排序。</summary>
    Task<IReadOnlyList<Trade>> GetAllAsync();

    /// <summary>
    /// 指定現金帳戶的交易記錄（含作為轉入目標的 Transfer）。
    /// </summary>
    Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid cashAccountId);

    /// <summary>
    /// 指定貸款名稱的 LoanBorrow / LoanRepay 記錄。
    /// </summary>
    Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string loanLabel);

    /// <summary>依 Id 取單筆交易；不存在回傳 <see langword="null"/>。</summary>
    Task<Trade?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(Trade trade);
    Task UpdateAsync(Trade trade);
    Task RemoveAsync(Guid id);

    /// <summary>
    /// 刪除所有 <see cref="Trade.ParentTradeId"/> == <paramref name="parentId"/> 的子記錄。
    /// 用於主交易刪除時連帶清除手續費等附屬 Withdrawal。
    /// </summary>
    Task RemoveChildrenAsync(Guid parentId);

    /// <summary>刪除所有引用指定帳戶（cash_account_id 或 to_cash_account_id）的交易記錄。</summary>
    Task RemoveByAccountIdAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// 刪除所有引用指定負債的交易記錄。信用卡走 <c>liability_asset_id</c>，貸款走 <c>loan_label</c>。
    /// 至少要提供其中一個；兩個皆有時 OR 條件套用。
    /// </summary>
    Task RemoveByLiabilityAsync(Guid? liabilityAssetId, string? loanLabel, CancellationToken ct = default);
}
