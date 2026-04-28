namespace Assetra.Core.Models;

/// <summary>
/// 投資組合每日快照。<see cref="Currency"/> 為 v0.14.2 新增 — 標示
/// <see cref="TotalCost"/> / <see cref="MarketValue"/> / <see cref="Pnl"/> 是以何種幣別記錄；
/// 舊資料 / 預設值為 "TWD"。下游分析（例如 MWR）若 base currency 不同，需透過 FX 轉換。
/// </summary>
public sealed record PortfolioDailySnapshot(
    DateOnly SnapshotDate,
    decimal TotalCost,
    decimal MarketValue,
    decimal Pnl,
    int PositionCount,
    string Currency = "TWD");
