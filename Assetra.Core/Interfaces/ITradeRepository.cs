using Assetra.Core.Models;

namespace Assetra.Core.Interfaces;

public interface ITradeRepository
{
    /// <summary>全部交易記錄，依日期降冪排序。</summary>
    Task<IReadOnlyList<Trade>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// 取出指定時間區間（含端點）的交易記錄。預設實作以 <see cref="GetAllAsync"/> 全量取出後 in-memory filter；
    /// SQLite 實作應 override 為 SQL WHERE 條件，避免大資料集全量載入。
    /// </summary>
    async Task<IReadOnlyList<Trade>> GetByPeriodAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var all = await GetAllAsync(ct).ConfigureAwait(false);
        return all.Where(t => t.TradeDate >= from && t.TradeDate <= to).ToList();
    }

    /// <summary>
    /// 取出 <see cref="Trade.PortfolioEntryId"/> 屬於 <paramref name="entryIds"/> 集合的交易。
    /// 預設實作 fallback 為 <see cref="GetAllAsync"/> + in-memory filter；SQLite 應 override 為 SQL <c>IN</c>。
    /// </summary>
    async Task<IReadOnlyList<Trade>> GetByPortfolioEntryIdsAsync(
        IReadOnlyCollection<Guid> entryIds, CancellationToken ct = default)
    {
        if (entryIds.Count == 0) return Array.Empty<Trade>();
        var set = entryIds.ToHashSet();
        var all = await GetAllAsync(ct).ConfigureAwait(false);
        return all.Where(t => t.PortfolioEntryId.HasValue && set.Contains(t.PortfolioEntryId.Value)).ToList();
    }

    /// <summary>
    /// 指定現金帳戶的交易記錄（含作為轉入目標的 Transfer）。
    /// </summary>
    Task<IReadOnlyList<Trade>> GetByCashAccountAsync(Guid cashAccountId, CancellationToken ct = default);

    /// <summary>
    /// 指定貸款名稱的 LoanBorrow / LoanRepay 記錄。
    /// </summary>
    Task<IReadOnlyList<Trade>> GetByLoanLabelAsync(string loanLabel, CancellationToken ct = default);

    /// <summary>依 Id 取單筆交易；不存在回傳 <see langword="null"/>。</summary>
    Task<Trade?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(Trade trade, CancellationToken ct = default);
    Task UpdateAsync(Trade trade, CancellationToken ct = default);
    Task RemoveAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// 刪除所有 <see cref="Trade.ParentTradeId"/> == <paramref name="parentId"/> 的子記錄。
    /// 用於主交易刪除時連帶清除手續費等附屬 Withdrawal。
    /// </summary>
    Task RemoveChildrenAsync(Guid parentId, CancellationToken ct = default);

    /// <summary>刪除所有引用指定帳戶（cash_account_id 或 to_cash_account_id）的交易記錄。</summary>
    Task RemoveByAccountIdAsync(Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// 刪除所有引用指定負債的交易記錄。信用卡走 <c>liability_asset_id</c>，貸款走 <c>loan_label</c>。
    /// 至少要提供其中一個；兩個皆有時 OR 條件套用。
    /// </summary>
    Task RemoveByLiabilityAsync(Guid? liabilityAssetId, string? loanLabel, CancellationToken ct = default);

    /// <summary>
    /// 在單一 SQLite transaction 中套用一批 <see cref="TradeMutation"/>。
    /// 任一筆失敗就 rollback，不會留下半套用狀態。供 ImportRollbackService 等需要原子性的場景使用。
    /// </summary>
    Task ApplyAtomicAsync(IReadOnlyList<TradeMutation> mutations, CancellationToken ct = default);
}
