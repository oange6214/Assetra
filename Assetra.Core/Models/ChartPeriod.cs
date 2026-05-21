namespace Assetra.Core.Models;

/// <summary>
/// 圖表時間視窗。原本 4 個視窗 (1M/3M/1Y/2Y) 為主要使用情境；
/// P4.7 加入 FiveDays / SixMonths / FiveYears / Max 對齊 AscentPortfolio
/// 圖表 chip 規格。
/// <para>
/// <b>Max</b>：實作上以 10 年 (120 個月) 作為「實質無上限」的近似——
/// 各 provider 不會無限制往回拉（Twse / FinMind 走月份分頁，5Y+ 很快
/// 撞 rate limit）；Cached layer 會把每天的 OHLC 快取下來，所以重複
/// 切視窗不會每次都打外網。
/// </para>
/// </summary>
public enum ChartPeriod
{
    FiveDays,
    OneMonth,
    ThreeMonths,
    SixMonths,
    OneYear,
    TwoYears,
    FiveYears,
    Max,
}
