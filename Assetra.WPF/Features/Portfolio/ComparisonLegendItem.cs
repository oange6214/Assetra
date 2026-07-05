namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// 「vs 大盤」% 對比圖的單一圖例項（chip）：線的標籤、顏色（hex）、可移除對標的 symbol。
/// <para><c>RemoveSymbol</c> null＝固定項（我的投組、大盤）、非 null＝自訂對標（chip 顯示 ✕）。</para>
/// <para><c>IsUnavailable</c>＝該比較項已加入清單、但這個區間畫不出線（買賣不成對、查無價格、
/// 盤中不適用…）。chip 仍會顯示（灰色 ＋ ⚠ ＋ <c>UnavailableReason</c> tooltip），讓使用者知道
/// 「有加進去、但為什麼沒有線」，並可移除——而不是整個項目靜默消失。</para>
/// 供 TrendsView chips ItemsControl 綁定。
/// </summary>
public sealed record ComparisonLegendItem(
    string Label,
    string ColorHex,
    string? RemoveSymbol = null,
    bool IsUnavailable = false,
    string? UnavailableReason = null);
