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

    Task AddAsync(Trade trade);
    Task UpdateAsync(Trade trade);
    Task RemoveAsync(Guid id);

    /// <summary>
    /// 刪除所有 <see cref="Trade.ParentTradeId"/> == <paramref name="parentId"/> 的子記錄。
    /// 用於主交易刪除時連帶清除手續費等附屬 Withdrawal。
    /// </summary>
    Task RemoveChildrenAsync(Guid parentId);
}
