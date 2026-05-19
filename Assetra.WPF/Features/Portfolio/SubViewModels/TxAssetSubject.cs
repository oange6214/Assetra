namespace Assetra.WPF.Features.Portfolio.SubViewModels;

/// <summary>
/// AddRecordDialog Phase 2 — 統一的「資產選擇器」item，把 PortfolioEntry / CashAccount /
/// Liability 三類資產收成同一個下拉清單的 row。
///
/// <para>
/// 後續（P2.2）會根據選了哪個 Kind 自動帶幣別、推薦現金帳戶；P2.3 會用 Kind 過濾可選的
/// trade type；P2.4 把單價/總額/取得市價 link 提升到通用區。本檔僅定義資料形狀，
/// 不含連動邏輯。
/// </para>
/// </summary>
public sealed record TxAssetSubject(
    TxAssetKind Kind,
    /// <summary>PortfolioEntry / CashAccount / Liability 的 row id；資產類為持倉時是
    /// PortfolioEntry.Id（不是 Trade.Id）。</summary>
    System.Guid Id,
    /// <summary>顯示字串：「NVDA · NVIDIA Corp」/「USD Savings · USD」/「台新 7y B · TWD」。
    /// ItemTemplate 直接綁這個。</summary>
    string Display,
    /// <summary>建議的計價幣別（ISO 4217）。Stock 由 exchange registry 推導、
    /// CashAccount / Liability 由 row 自帶。P2.2 起 OnSelectedAssetChanged 會把它複製到
    /// dialog 的 TxCurrency。</summary>
    string Currency,
    /// <summary>股票/基金等資產才有；其他類為 null。</summary>
    string? Symbol = null,
    /// <summary>建議的現金帳戶 id（同幣別、user-default 優先）。P2.2 cascade 用。</summary>
    System.Guid? SuggestedCashAccountId = null);

/// <summary>
/// <see cref="TxAssetSubject.Kind"/> — 決定 trade type 過濾規則。對應 PortfolioEntry /
/// CashAccount / Liability 三大來源。
/// </summary>
public enum TxAssetKind
{
    /// <summary>尚未選擇 — type ComboBox disabled。</summary>
    None,
    /// <summary>股票 / ETF。可選 type：買入 / 賣出 / 現金股利 / 股票股利。</summary>
    Stock,
    /// <summary>基金。可選 type：買入 / 賣出 / 現金股利。</summary>
    Fund,
    /// <summary>加密幣。可選 type：買入 / 賣出。</summary>
    Crypto,
    /// <summary>貴金屬。可選 type：買入 / 賣出。</summary>
    Metal,
    /// <summary>債券。可選 type：買入 / 賣出 / 現金股利（票息）。</summary>
    Bond,
    /// <summary>現金帳戶（含信用卡的 cash 面相對等）。可選 type：收入 / 存入 / 提款 / 轉帳。</summary>
    CashAccount,
    /// <summary>負債（信用卡 / 貸款）。可選 type：借款 / 還款 / 信用卡刷卡 / 信用卡還款。</summary>
    Liability,
}
