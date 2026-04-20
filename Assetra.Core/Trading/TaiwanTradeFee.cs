namespace Assetra.Core.Trading;

/// <summary>
/// Fee breakdown for a single Taiwan stock transaction.
/// All amounts are in TWD, rounded to the nearest integer (no partial NT$).
/// </summary>
public sealed record TaiwanTradeFee(
    decimal GrossAmount,      // 成交金額  price × qty
    decimal Commission,       // 手續費    (min 20 TWD)
    decimal TransactionTax,   // 證券交易稅 (賣出才有；買入為 0)
    decimal NetAmount,        // 實際金額  buy: gross+commission  sell: gross-commission-tax
    bool IsEtf);
