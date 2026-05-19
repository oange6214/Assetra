namespace Assetra.WPF.Features.FinancialOverview;

/// <summary>
/// One row in the「資產組成」composition view that replaces the older 6-cell
/// AssetClassFocusWidget. Filtered to non-zero asset classes and sorted by
/// <see cref="Value"/> descending, so the largest holdings sit at the top.
///
/// <para>
/// <see cref="ColorHex"/> is a hex string (NOT a brush key) so XAML can resolve
/// it via a <c>StringToBrushConverter</c>. Hex is preferred over a theme-token
/// key here because each asset class has a fixed semantic colour
/// (投資=藍 / 現金=綠 / 不動產=紫 …) that doesn't switch with light/dark theme.
/// </para>
///
/// <para>
/// <see cref="NavigateTag"/> is the asset-class key used by the existing
/// <c>NavigateToXxxCommand</c> family on <c>FinancialOverviewViewModel</c>;
/// the row binds <c>MouseBinding</c> to dispatch on this tag.
/// </para>
/// </summary>
public sealed record AssetCompositionSlice(
    string NameKey,
    decimal Value,
    string ColorHex,
    string? NavigateTag,
    decimal Percent = 0m,
    decimal Total = 0m,
    string BaseCurrency = "TWD")
{
    public string ValueDisplay => MoneyFormatter.Format(Value, BaseCurrency);
    public string PercentDisplay => Percent.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "%";
}
