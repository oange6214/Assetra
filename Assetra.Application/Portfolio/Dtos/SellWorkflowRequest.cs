using Assetra.Core.Models;

namespace Assetra.Application.Portfolio.Dtos;

public sealed record SellWorkflowRequest(
    Guid PortfolioEntryId,
    string Symbol,
    string Exchange,
    string Name,
    decimal BuyPrice,
    int CurrentQuantity,
    int SellQuantity,
    decimal SellPrice,
    DateTime TradeDate,
    decimal Commission,
    decimal? CommissionDiscount,
    Guid? CashAccountId,
    IReadOnlyList<Guid> EntryIdsToArchive,
    // MultiCurrency-Trade-Refactor P3 — 跨幣別賣出時填券商實際入帳金額（帳戶幣別）
    // 與 FxRate（標的幣別 → 帳戶幣別）。同幣別交易兩者皆 null。
    decimal? ActualCashAmount = null,
    decimal? FxRate = null,
    // Portfolio-Groups-Refactor P3
    Guid? PortfolioGroupId = null);

public sealed record SellWorkflowResult(
    Trade Trade,
    int RemainingQuantity);
