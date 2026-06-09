using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Dtos;

public sealed record ClosePriceLookupResult(
    bool HasPrice,
    decimal? Price,
    string Hint);

public sealed record BuyPreviewRequest(
    string Symbol,
    decimal Price,
    int Quantity,
    decimal CommissionDiscount,
    decimal? ManualFee,
    string? Exchange = null);

public sealed record BuyPreviewResult(
    decimal GrossAmount,
    decimal Commission,
    decimal TotalCost,
    decimal CostPerShare);

public sealed record EnsureStockEntryRequest(
    string Symbol,
    string? Exchange = null,
    string? Name = null,
    // Portfolio-Groups-Refactor P3 — 新 entry 預設群組（bucket）。null = DefaultId fallback。
    Guid? PortfolioGroupId = null,
    // Watchlist refactor — 觀察標的可指定資產類型 (Fund/Bond/Crypto/PreciousMetal/Etf/Stock)
    // 與計價幣別。Buy / Sell 等實際交易流程不會傳這兩個（預設 Stock + 自動推導 currency），
    // 觀察清單對話框會明確帶入。
    AssetType AssetType = AssetType.Stock,
    string? Currency = null);

public sealed record StockBuyRequest(
    string Symbol,
    decimal Price,
    int Quantity,
    DateTime BuyDate,
    Guid? CashAccountId,
    decimal CommissionDiscount,
    decimal? ManualFee = null,
    string? Exchange = null,
    string? Name = null,
    decimal? ActualCashAmount = null,
    // MultiCurrency-Trade-Refactor P3 — 跨幣別 FX rate（標的幣別 → 帳戶幣別）。
    // 同幣別交易留 null（implicit 1.0）。XAML 在跨幣別 Mode 才暴露此欄位。
    // 若兩者皆填，ActualCashAmount 為權威（直接寫入 Trade.CashAmount），FxRate 僅供
    // 報表時還原成本基礎使用。
    decimal? FxRate = null,
    string? SettlementCurrency = null,
    DateOnly? FxRateDate = null,
    string? FxSource = null,
    // Portfolio-Groups-Refactor P3 — 此筆交易所屬群組（bucket）。
    // null = 沿用 PortfolioGroup.DefaultId（Trade 寫入時 fallback）。
    Guid? PortfolioGroupId = null);

public sealed record StockBuyResult(
    PortfolioEntry Entry,
    decimal Commission,
    decimal? CommissionDiscountUsed,
    decimal CostPerShare);

public sealed record ManualAssetCreateRequest(
    string Symbol,
    string Exchange,
    string Name,
    AssetType AssetType,
    decimal Quantity,
    decimal TotalCost,
    decimal UnitPrice,
    DateOnly AcquiredOn,
    // Portfolio-Groups-Refactor P3
    Guid? PortfolioGroupId = null);

public sealed record ManualAssetCreateResult(
    PortfolioEntry Entry,
    PositionSnapshot Snapshot);

/// <summary>
/// 新增投資對話框「即時報價可用性」提示用的代號判定結果（純讀取、不寫入）。
/// <para><see cref="IsResolved"/>：代號是否能在 symbol-directory 解析到（已知商品）。</para>
/// <para><see cref="UsDirectoryReady"/>：美股代號目錄（NASDAQ 等）是否已下載且有資料。
/// false 代表目錄尚未下載或為空，無法用來判斷美股代號是否存在。</para>
/// </summary>
public sealed record WatchlistSymbolReadiness(
    bool IsResolved,
    bool UsDirectoryReady);
