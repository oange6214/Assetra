namespace Assetra.WPF.Features.Portfolio;

/// <summary>
/// 資產趨勢比較圖「下方清單」的一列：項目標籤、顏色（hex）、移除 token、以及「截至顯示日」的同期報酬 %
/// （自區間起點起算，double）。顯示日預設為期末；滑鼠 hover 圖表時改為 hover 到的那天。
/// </summary>
public sealed record ComparisonRow(string Label, string ColorHex, string? RemoveToken, double Percent);
