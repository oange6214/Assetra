namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// 「vs 大盤」% 對比圖的單一圖例項（chip）：線的標籤、顏色（hex）、可移除對標的 symbol。
/// <para><c>RemoveSymbol</c> null＝固定項（我的投組、大盤）、非 null＝自訂對標（chip 顯示 ✕）。</para>
/// 供 TrendsView chips ItemsControl 綁定。
/// </summary>
public sealed record ComparisonLegendItem(string Label, string ColorHex, string? RemoveSymbol = null);
