using Assetra.Core.Models;

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
///
/// <para>
/// M1 — 回傳型別已從 <c>decimal</c> 改為 <see cref="Money"/>。每筆現金餘額會帶上該
/// <see cref="AssetItem.Currency"/> tag；負債餘額帶上負債資產的幣別（沒有 asset
/// metadata 的歷史 loan label 預設為 <see cref="DefaultCurrency"/>）。
/// </para>
/// </summary>
public interface IBalanceQueryService
{
    /// <summary>本服務在無 asset metadata 時使用的預設幣別 tag。</summary>
    public const string DefaultCurrency = "TWD";

    /// <summary>
    /// 計算指定現金帳戶的即時餘額，幣別取自該 <see cref="AssetItem.Currency"/>。
    /// 帳戶不存在或從無交易時回傳 <see cref="Money.Zero(string)"/>(<see cref="DefaultCurrency"/>)。
    /// </summary>
    Task<Money> GetCashBalanceAsync(Guid cashAccountId);

    /// <summary>
    /// 計算指定貸款名稱的當前餘額與原始借款總額。
    /// 名稱不存在或從無交易時兩者皆為 Zero。
    /// </summary>
    Task<LiabilitySnapshot> GetLiabilitySnapshotAsync(string loanLabel);

    /// <summary>
    /// 一次掃過所有交易，回傳每個現金帳戶的投影餘額。
    /// UI 載入清單時應使用此方法，避免 O(n×m)。
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Money>> GetAllCashBalancesAsync();

    /// <summary>
    /// 一次掃過所有交易，回傳每個貸款名稱的投影餘額與原始金額。
    /// </summary>
    Task<IReadOnlyDictionary<string, LiabilitySnapshot>> GetAllLiabilitySnapshotsAsync();

    /// <summary>
    /// 與 <see cref="GetAllCashBalancesAsync"/> 相同投影，但只計入交易日 ≤ <paramref name="asOf"/>
    /// 的交易（日期以 <c>DateOnly.FromDateTime(Trade.TradeDate)</c> 為準，沿用全專案慣例）。
    /// 供歷史快照重建取得「過去某交易日收盤後」的現金餘額。
    /// <para>當 <paramref name="asOf"/> ≥ 最後一筆交易日時，結果與 <see cref="GetAllCashBalancesAsync"/> 相同。</para>
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Money>> GetAllCashBalancesAsOfAsync(
        DateOnly asOf, CancellationToken ct = default);

    /// <summary>
    /// 與 <see cref="GetAllLiabilitySnapshotsAsync"/> 相同投影，但只計入交易日 ≤ <paramref name="asOf"/>
    /// 的交易（同 <see cref="GetAllCashBalancesAsOfAsync"/> 的日期慣例）。
    /// 供歷史快照重建取得「過去某交易日收盤後」的負債餘額。
    /// </summary>
    Task<IReadOnlyDictionary<string, LiabilitySnapshot>> GetAllLiabilitySnapshotsAsOfAsync(
        DateOnly asOf, CancellationToken ct = default);
}

/// <summary>
/// 負債帳戶的投影結果：當前餘額 + 原始借款總額（用於計算還款百分比）。
/// 兩個欄位共用同一幣別（同一筆 loan 內所有 trade 必為同幣別）。
/// </summary>
public readonly record struct LiabilitySnapshot(Money Balance, Money OriginalAmount)
{
    /// <summary>常用的「無資料」回傳值，幣別預設為 <see cref="IBalanceQueryService.DefaultCurrency"/>。</summary>
    public static readonly LiabilitySnapshot Empty =
        new(Money.Zero(IBalanceQueryService.DefaultCurrency), Money.Zero(IBalanceQueryService.DefaultCurrency));

    /// <summary>建立指定幣別的零值快照（投影累加的起始狀態）。</summary>
    public static LiabilitySnapshot ZeroOf(string currency) =>
        new(Money.Zero(currency), Money.Zero(currency));
}
