using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio.SubViewModels.Tx;

/// <summary>
/// H1 — first child VM split off from <c>TransactionDialogViewModel</c>. Owns the
/// **buy transaction state cluster**:
/// <list type="bullet">
///   <item><see cref="AssetType"/>: stock / fund / metal / bond / crypto</item>
///   <item><see cref="MetaOnly"/>: watch-list mode (skip Buy trade row, Qty=0 position)</item>
///   <item><see cref="PriceMode"/>: unit-price vs total-amount input</item>
///   <item><see cref="TotalCost"/> + <see cref="TotalIncludesFee"/>: total-amount fields</item>
///   <item>Validation error string + computed display helpers</item>
/// </list>
///
/// <para>
/// Phase 2: state moves here, dialog VM keeps facade properties so XAML / tests stay green.
/// Phase 3: XAML migrated to <c>Buy.X</c> bindings.
/// Phase 4: facades deleted from dialog VM, completing the cluster split.
/// </para>
/// </summary>
public sealed partial class BuyTxViewModel : ObservableObject
{
    /// <summary>Asset category — drives which input panel the form shows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStock))]
    [NotifyPropertyChangedFor(nameof(IsNonStock))]
    [NotifyPropertyChangedFor(nameof(IsCrypto))]
    private string _assetType = "stock";

    /// <summary>
    /// Watchlist-only mode — when true, ConfirmBuy writes only the PortfolioEntry
    /// and skips the Buy trade row, producing a Qty=0 position with no balance impact.
    /// (Plan Task 18 — Option B.)
    /// </summary>
    [ObservableProperty] private bool _metaOnly;

    /// <summary>"unit" = user enters per-share price; "total" = user enters total amount.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnitMode))]
    [NotifyPropertyChangedFor(nameof(IsTotalMode))]
    private string _priceMode = "unit";

    /// <summary>「總額」模式下使用者輸入的總成交金額。</summary>
    [ObservableProperty] private string _totalCost = string.Empty;

    /// <summary>
    /// 「總額」模式下，輸入的總額是否已包含手續費。預設 true（多數券商
    /// 給的成交回報是含手續費的最終扣款金額）。
    /// </summary>
    [ObservableProperty] private bool _totalIncludesFee = true;

    /// <summary>正/負數驗證錯誤訊息（空字串 = 通過）。</summary>
    [ObservableProperty] private string _totalCostError = string.Empty;

    public bool IsStock => AssetType == "stock";
    public bool IsNonStock => AssetType is "fund" or "metal" or "bond";
    public bool IsCrypto => AssetType == "crypto";

    public bool IsUnitMode => PriceMode == "unit";
    public bool IsTotalMode => PriceMode == "total";

    /// <summary>
    /// 總計顯示文字。
    /// 總額模式 → 顯示使用者輸入的總額。
    /// 單價模式 → 顯示 數量 × 單價，由 caller 提供 price/qty。
    /// </summary>
    public string ComputeTotalDisplay(string addPriceText, string addQuantityText)
    {
        if (IsTotalMode &&
            ParseHelpers.TryParseDecimal(TotalCost, out var t) && t > 0)
            return t.ToString("N0");
        if (ParseHelpers.TryParseDecimal(addPriceText, out var p) && p > 0 &&
            ParseHelpers.TryParseInt(addQuantityText, out var q) && q > 0)
            return (p * q).ToString("N0");
        return "0";
    }

    /// <summary>重置所有買入欄位回預設（dialog 開新交易時呼叫）。</summary>
    public void Reset()
    {
        AssetType = "stock";
        MetaOnly = false;
        PriceMode = "unit";
        TotalCost = string.Empty;
        TotalIncludesFee = true;  // most broker totals include fee
        TotalCostError = string.Empty;
    }
}
