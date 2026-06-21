namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// 「vs 大盤」% 對比圖的單一圖例項：線的標籤與顏色（hex）。供 TrendsView 圖例 ItemsControl 綁定。
/// </summary>
public sealed record ComparisonLegendItem(string Label, string ColorHex);
