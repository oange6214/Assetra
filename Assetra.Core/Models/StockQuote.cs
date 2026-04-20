namespace Assetra.Core.Models;

public record StockQuote(
    string Symbol,
    string Name,
    string Exchange,      // "TWSE" | "TPEX"
    decimal Price,
    decimal Change,
    decimal ChangePercent,
    long Volume,          // in 張
    decimal Open,
    decimal High,
    decimal Low,
    decimal PrevClose,
    DateTimeOffset UpdatedAt);
