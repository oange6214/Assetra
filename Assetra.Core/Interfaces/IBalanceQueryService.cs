namespace Assetra.Core.Interfaces;

/// <summary>
/// 單一真相來源（single source of truth）：帳戶餘額僅由交易記錄（Trade journal）投影而來。
///
/// <para>
/// 帳戶本身（<c>CashAccount</c>）不再儲存餘額。
/// 所有餘額查詢都必須透過此服務，直接由 <c>ITradeRepository</c> 的歷史資料計算。
/// </para>
///
/// <para>投影規則：</para>
/// <list type="bullet">
///   <item>Cash = Σ PrimaryCashDelta(t where CashAccountId = id) + Σ CashAmount(t where Type=Transfer AND ToCashAccountId = id)</item>
///   <item>Liability.Balance        = Σ LoanBorrow.CashAmount − Σ LoanRepay.Principal  （按 LoanLabel 分組）</item>
///   <item>Liability.OriginalAmount = Σ LoanBorrow.CashAmount                          （按 LoanLabel 分組）</item>
/// </list>
/// </summary>
public interface IBalanceQueryService
{
    /// <summary>
    /// 計算指定現金帳戶的即時餘額。
    /// 帳戶不存在或從無交易時回傳 0。
    /// </summary>
    Task<decimal> GetCashBalanceAsync(Guid cashAccountId);

    /// <summary>
    /// 計算指定貸款名稱的當前餘額與原始借款總額。
    /// 名稱不存在或從無交易時兩者皆為 0。
    /// </summary>
    Task<LiabilitySnapshot> GetLiabilitySnapshotAsync(string loanLabel);

    /// <summary>
    /// 一次掃過所有交易，回傳每個現金帳戶的投影餘額。
    /// UI 載入清單時應使用此方法，避免 O(n×m)。
    /// </summary>
    Task<IReadOnlyDictionary<Guid, decimal>> GetAllCashBalancesAsync();

    /// <summary>
    /// 一次掃過所有交易，回傳每個貸款名稱的投影餘額與原始金額。
    /// </summary>
    Task<IReadOnlyDictionary<string, LiabilitySnapshot>> GetAllLiabilitySnapshotsAsync();
}

/// <summary>
/// 負債帳戶的投影結果：當前餘額 + 原始借款總額（用於計算還款百分比）。
/// </summary>
public readonly record struct LiabilitySnapshot(decimal Balance, decimal OriginalAmount)
{
    public static readonly LiabilitySnapshot Empty = new(0m, 0m);
}
