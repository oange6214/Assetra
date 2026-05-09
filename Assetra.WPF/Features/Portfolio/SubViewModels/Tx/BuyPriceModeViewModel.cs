using Assetra.WPF.Infrastructure;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Assetra.WPF.Features.Portfolio.SubViewModels.Tx;

/// <summary>
/// H1 — first slice of the TransactionDialogViewModel god-object split.
/// Owns the **buy "price-input mode" cluster**: unit-price vs total-amount,
/// the total-cost text + include-fee flag, validation error text, and the
/// preview "computed total" display string.
///
/// <para>
/// The dialog VM still exposes facade properties (<c>TxBuyPriceMode</c>,
/// <c>TxBuyTotalCost</c>, …) that delegate here — so XAML bindings + tests
/// keep working. Future H1 phases will migrate XAML/test consumers to
/// <c>Buy.PriceMode</c>, <c>Buy.TotalCost</c>, etc., and the dialog facades
/// can then be deleted.
/// </para>
/// </summary>
public sealed partial class BuyPriceModeViewModel : ObservableObject
{
    /// <summary>"unit" = user enters per-share price; "total" = user enters total amount.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnitMode))]
    [NotifyPropertyChangedFor(nameof(IsTotalMode))]
    private string _priceMode = "unit";

    /// <summary>「總額」模式下使用者輸入的總成交金額。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ComputedTotalDisplay))]
    private string _totalCost = string.Empty;

    /// <summary>
    /// 「總額」模式下，輸入的總額是否已包含手續費。預設 true（多數券商
    /// 給的成交回報是含手續費的最終扣款金額）。
    /// </summary>
    [ObservableProperty] private bool _totalIncludesFee = true;

    /// <summary>正/負數驗證錯誤訊息（空字串 = 通過）。</summary>
    [ObservableProperty] private string _totalCostError = string.Empty;

    public bool IsUnitMode => PriceMode == "unit";
    public bool IsTotalMode => PriceMode == "total";

    /// <summary>
    /// 總計顯示文字（給 UI binding 用）。
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

    /// <summary>
    /// 純粹給 binding 的 placeholder — 永遠回 0；真實值請呼叫
    /// <see cref="ComputeTotalDisplay(string, string)"/> 並由 caller 觸發 PropertyChanged。
    /// </summary>
    public string ComputedTotalDisplay => ComputeTotalDisplay(string.Empty, string.Empty);

    /// <summary>重置回預設值（dialog 關閉/開新交易時呼叫）。</summary>
    public void Reset()
    {
        PriceMode = "unit";
        TotalCost = string.Empty;
        TotalIncludesFee = true;  // most broker totals include fee
        TotalCostError = string.Empty;
    }
}
