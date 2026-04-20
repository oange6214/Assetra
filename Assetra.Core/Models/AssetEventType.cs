namespace Assetra.Core.Models;

/// <summary>
/// Whether an asset event changes cost basis (Transaction)
/// or merely records a market-price update (Valuation).
/// </summary>
public enum AssetEventType { Transaction, Valuation }
