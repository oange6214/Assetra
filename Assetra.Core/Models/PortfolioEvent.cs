namespace Assetra.Core.Models;

/// <summary>
/// 投資組合時間軸上值得標註的事件 — 給趨勢圖（TrendsView）的 hover annotation 用。
/// 來源可以是自動偵測（large trade、首次配息）或使用者手動加註。
/// 純 value object，不含 portfolio 反向引用 — 由 caller 透過 <see cref="Date"/> 對齊。
/// </summary>
public sealed record PortfolioEvent(
    Guid Id,
    DateOnly Date,
    PortfolioEventKind Kind,
    string Label,
    string? Description,
    decimal? Amount = null,
    string? Symbol = null);

public enum PortfolioEventKind
{
    /// <summary>單筆買賣金額相對組合 ≥ threshold（例：≥ 10%）。</summary>
    LargeTrade = 0,
    /// <summary>該標的的首筆配息。</summary>
    FirstDividend = 1,
    /// <summary>年度新高 / 新低。</summary>
    YearlyExtreme = 2,
    /// <summary>市場層級事件（崩盤、央行決議等）— 通常為使用者手動加註。</summary>
    MarketEvent = 3,
    /// <summary>使用者自訂備註。</summary>
    UserNote = 4,
}
